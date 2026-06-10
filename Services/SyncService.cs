using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Supabase;
using TodoSidebar.Config;
using TodoSidebar.Models;

namespace TodoSidebar.Services
{
    /// <summary>
    /// 同步服务（v2 — 修复冲突解决/离线队列/增量同步/批量上传/Timer异常）
    /// </summary>
    public class SyncService : ISyncService
    {
        private static SyncService? _instance;
        private static readonly object _lock = new object();
        
        private PeriodicTimer? _syncTimer;
        private CancellationTokenSource? _cts;
        private Task? _syncLoopTask;
        private readonly DatabaseService _dbService = DatabaseService.Instance;
        private readonly AuthService _authService = AuthService.Instance;
        private readonly SyncLogService _syncLog = SyncLogService.Instance;
        private readonly NetworkMonitor _network = NetworkMonitor.Instance;
        private IFeatureFlagService? _featureFlags;
        
        // 增量同步：记录上次同步时间（UTC）
        private DateTime? _lastSyncTimeUtc;
        
        public static SyncService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new SyncService();
                        }
                    }
                }
                return _instance;
            }
        }
        
        /// <summary>
        /// 同步状态
        /// </summary>
        public SyncStatus Status { get; private set; } = SyncStatus.Idle;
        
        /// <summary>
        /// 最后同步时间
        /// </summary>
        public DateTime? LastSyncTime { get; private set; }
        
        /// <summary>
        /// 同步状态变化事件
        /// </summary>
        public event EventHandler<SyncStatus>? StatusChanged;
        
        /// <summary>
        /// 同步完成事件
        /// </summary>
        public event EventHandler<SyncResult>? SyncCompleted;
        
        private SyncService()
        {
        }
        
        /// <summary>
        /// 设置 Feature Flag 服务（由 DI 容器调用）
        /// </summary>
        public void SetFeatureFlags(IFeatureFlagService featureFlags)
        {
            _featureFlags = featureFlags;
        }
        
        /// <summary>
        /// 初始化同步服务
        /// </summary>
        public async Task InitializeAsync()
        {
            await SupabaseClientService.InitializeAsync();
            
            // 从数据库恢复上次同步时间
            var savedSyncTime = _dbService.GetSetting("LastSyncTimeUtc");
            if (!string.IsNullOrEmpty(savedSyncTime) && DateTime.TryParse(savedSyncTime, out var parsed))
            {
                _lastSyncTimeUtc = parsed;
            }
            
            // 用 PeriodicTimer 替代 System.Timers.Timer，正确处理 async
            _cts = new CancellationTokenSource();
            _syncTimer = new PeriodicTimer(TimeSpan.FromSeconds(SupabaseConfig.SyncIntervalSeconds));
            _syncLoopTask = RunSyncLoopAsync(_cts.Token);
            
            // 网络恢复时自动触发同步
            _network.ConnectivityChanged += async (_, online) =>
            {
                if (online && _authService.IsLoggedIn)
                {
                    System.Diagnostics.Debug.WriteLine("[SyncService] Network restored, triggering sync");
                    try { await SyncAsync(); } catch { }
                }
            };
        }
        
        /// <summary>
        /// 同步循环 — 用 PeriodicTimer 正确处理 async + 异常
        /// </summary>
        private async Task RunSyncLoopAsync(CancellationToken ct)
        {
            try
            {
                while (await _syncTimer!.WaitForNextTickAsync(ct))
                {
                    try
                    {
                        await SyncAsync();
                    }
                    catch (Exception ex)
                    {
                        // 单次同步失败不影响循环
                        System.Diagnostics.Debug.WriteLine($"Sync tick error: {ex.Message}");
                        SetStatus(SyncStatus.Error);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常退出，忽略
            }
        }
        
        /// <summary>
        /// 执行同步
        /// </summary>
        public async Task<SyncResult> SyncAsync()
        {
            if (Status == SyncStatus.Syncing)
                return new SyncResult { Success = false, Error = "正在同步中" };
            
            if (!AuthService.Instance.IsLoggedIn)
                return new SyncResult { Success = false, Error = "未登录" };
            
            // 离线检查
            if (!_network.IsOnline)
            {
                _syncLog.Log(new SyncLogEntry
                {
                    Action = "sync",
                    Success = false,
                    Details = "已离线，跳过同步"
                });
                return new SyncResult { Success = false, Error = "已离线" };
            }
            
            SetStatus(SyncStatus.Syncing);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            
            try
            {
                var result = new SyncResult();
                
                // 1. 上传本地更改（批量）
                result.Uploaded = await UploadLocalChangesAsync();
                
                // 2. 下载远程更改（增量 + 冲突解决）
                var downloadResult = await DownloadRemoteChangesAsync();
                result.Downloaded = downloadResult.downloaded;
                result.Conflicts = downloadResult.conflicts;
                
                // 3. 定期清理软删除记录（30天前的）
                _dbService.PurgeDeletedTasks(30);
                
                result.Success = true;
                LastSyncTime = DateTime.Now;
                
                // 保存同步时间到数据库
                _lastSyncTimeUtc = DateTime.UtcNow;
                _dbService.SetSetting("LastSyncTimeUtc", _lastSyncTimeUtc.Value.ToString("O"));
                
                SetStatus(SyncStatus.Idle);
                SyncCompleted?.Invoke(this, result);
                
                sw.Stop();
                _syncLog.Log(new SyncLogEntry
                {
                    Action = "sync",
                    Success = true,
                    Uploaded = result.Uploaded,
                    Downloaded = result.Downloaded,
                    Conflicts = result.Conflicts,
                    Duration = sw.Elapsed,
                    Details = $"上传{result.Uploaded}条，下载{result.Downloaded}条，冲突{result.Conflicts}条"
                });
                
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Sync error: {ex.Message}");
                SetStatus(SyncStatus.Error);
                
                sw.Stop();
                _syncLog.Log(new SyncLogEntry
                {
                    Action = "sync",
                    Success = false,
                    ErrorMessage = ex.Message,
                    Duration = sw.Elapsed
                });
                
                return new SyncResult { Success = false, Error = ex.Message };
            }
        }
        
        /// <summary>
        /// 上传本地更改（批量 upsert）
        /// </summary>
        public async Task<int> UploadLocalChangesAsync()
        {
            try
            {
                var dirtyTasks = _dbService.GetDirtyTasks();
                if (dirtyTasks.Count == 0)
                    return 0;
                
                var client = SupabaseClientService.Client;
                var userId = _authService.CurrentUser?.Id;
                if (string.IsNullOrEmpty(userId))
                    return 0;
                
                // 构建批量同步列表
                var syncTasks = new List<SyncTask>();
                var taskMapping = new List<(int localId, SyncTask syncTask)>();
                
                foreach (var task in dirtyTasks)
                {
                    var syncId = string.IsNullOrEmpty(task.SyncId) ? Guid.NewGuid() : Guid.Parse(task.SyncId);
                    var syncTask = new SyncTask
                    {
                        Id = syncId,
                        UserId = userId,
                        Title = task.Title,
                        Type = (int)task.Type,
                        Priority = (int)task.Priority,
                        IsCompleted = task.IsCompleted,
                        CreatedAt = task.CreatedAt,
                        Deadline = task.Deadline,
                        CompletedAt = task.CompletedAt,
                        Description = task.Description,
                        Tags = task.Tags,
                        SortOrder = task.SortOrder,
                        SubtasksJson = task.SubTasksJson,
                        UpdatedAt = DateTime.UtcNow,
                        IsDeleted = task.IsDeleted
                    };
                    
                    syncTasks.Add(syncTask);
                    taskMapping.Add((task.Id, syncTask));
                }
                
                // 批量 upsert（一次 HTTP 请求）
                try
                {
                    await client.From<SyncTask>().Upsert(syncTasks);
                    
                    // 全部成功，标记本地任务已同步
                    foreach (var (localId, syncTask) in taskMapping)
                    {
                        _dbService.MarkTaskSynced(localId, syncTask.Id.ToString());
                    }
                    
                    return syncTasks.Count;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Batch upload error: {ex.Message}");
                    
                    // 批量失败时逐条重试（指数退避）
                    int uploaded = 0;
                    int retryDelay = 500; // 初始 500ms
                    foreach (var (localId, syncTask) in taskMapping)
                    {
                        try
                        {
                            await client.From<SyncTask>().Upsert(syncTask);
                            _dbService.MarkTaskSynced(localId, syncTask.Id.ToString());
                            uploaded++;
                            retryDelay = 500; // 成功则重置
                        }
                        catch (Exception itemEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"Upload task {localId} error: {itemEx.Message}");
                            // IsDirty 保持为1，下次同步会重试
                            await Task.Delay(retryDelay);
                            retryDelay = Math.Min(retryDelay * 2, 5000); // 最大 5 秒
                        }
                    }
                    
                    return uploaded;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UploadLocalChanges error: {ex.Message}");
                return 0;
            }
        }
        
        /// <summary>
        /// 下载远程更改（增量 + LWW 冲突解决）
        /// </summary>
        public async Task<(int downloaded, int conflicts)> DownloadRemoteChangesAsync()
        {
            try
            {
                var client = SupabaseClientService.Client;
                var userId = _authService.CurrentUser?.Id;
                if (string.IsNullOrEmpty(userId))
                    return (0, 0);
                
                // 增量同步：只拉取上次同步后有更新的任务
                Supabase.Postgrest.Responses.ModeledResponse<SyncTask> response;
                
                if (_lastSyncTimeUtc.HasValue)
                {
                    // 拉取上次同步后更新的任务（包括新创建的和已删除的）
                    response = await client.From<SyncTask>()
                        .Where(x => x.UserId == userId && x.UpdatedAt >= _lastSyncTimeUtc.Value)
                        .Get();
                }
                else
                {
                    // 首次同步：拉取所有任务
                    response = await client.From<SyncTask>()
                        .Where(x => x.UserId == userId)
                        .Get();
                }
                
                var remoteTasks = response.Models;
                
                if (remoteTasks == null || remoteTasks.Count() == 0)
                    return (0, 0);
                
                int downloaded = 0;
                int conflicts = 0;
                
                foreach (var remoteTask in remoteTasks)
                {
                    try
                    {
                        var localTask = new TaskItem
                        {
                            SyncId = remoteTask.Id.ToString(),
                            Title = remoteTask.Title,
                            Type = (TaskType)remoteTask.Type,
                            Priority = (TaskPriority)remoteTask.Priority,
                            IsCompleted = remoteTask.IsCompleted,
                            CreatedAt = remoteTask.CreatedAt,
                            Deadline = remoteTask.Deadline,
                            CompletedAt = remoteTask.CompletedAt,
                            Description = remoteTask.Description,
                            Tags = remoteTask.Tags,
                            SortOrder = remoteTask.SortOrder,
                            SubTasksJson = remoteTask.SubtasksJson,
                            IsDeleted = remoteTask.IsDeleted,
                            IsDirty = false,
                            LastSyncedAt = DateTime.Now
                        };
                        
                        // LWW 冲突解决
                        var existing = _dbService.GetTaskBySyncId(remoteTask.Id.ToString());
                        
                        if (existing != null && existing.IsDirty)
                        {
                            // 冲突：本地有未同步的修改 + 远程也有修改
                            if (remoteTask.UpdatedAt > existing.LastSyncedAt)
                            {
                                // 远程更新，覆盖本地
                                _dbService.UpsertTaskFromRemote(localTask);
                                downloaded++;
                                
                                _syncLog.Log(new SyncLogEntry
                                {
                                    Action = "conflict",
                                    Success = true,
                                    Details = $"冲突解决(LWW-远程胜): \"{existing.Title}\" → 远程覆盖本地"
                                });
                            }
                            else
                            {
                                // 本地更新，保留本地
                                conflicts++;
                                
                                _syncLog.Log(new SyncLogEntry
                                {
                                    Action = "conflict",
                                    Success = true,
                                    Details = $"冲突解决(LWW-本地胜): \"{existing.Title}\" → 保留本地版本"
                                });
                            }
                        }
                        else
                        {
                            // 无冲突：直接更新/插入
                            _dbService.UpsertTaskFromRemote(localTask);
                            downloaded++;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Download task {remoteTask.Id} error: {ex.Message}");
                    }
                }
                
                return (downloaded, conflicts);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DownloadRemoteChanges error: {ex.Message}");
                return (0, 0);
            }
        }
        
        /// <summary>
        /// 手动触发同步（UI 调用）
        /// </summary>
        public async Task<SyncResult> ManualSyncAsync()
        {
            return await SyncAsync();
        }
        
        /// <summary>
        /// 设置同步状态
        /// </summary>
        private void SetStatus(SyncStatus status)
        {
            Status = status;
            StatusChanged?.Invoke(this, status);
        }
        
        /// <summary>
        /// 停止同步服务
        /// </summary>
        public void Stop()
        {
            _cts?.Cancel();
            _syncTimer?.Dispose();
            _cts?.Dispose();
        }
    }
}
