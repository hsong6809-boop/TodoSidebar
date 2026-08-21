using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        
        // 防重入：使用 Interlocked 作为轻量级同步锁
        private int _syncInProgress = 0;
        
        // 网络恢复事件处理器（保存引用以便取消订阅）
        private EventHandler<bool>? _networkHandler;
        
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
            // 防止重复初始化
            if (_cts != null) return;

            await SupabaseClientService.InitializeAsync();

            // 从数据库恢复上次同步时间（S8 修复：游标按用户隔离，
            // 避免切换账号后沿用前一用户的增量游标导致云端任务大面积不下载）
            var userId = _authService.CurrentUser?.Id;
            if (!string.IsNullOrEmpty(userId))
            {
                var savedSyncTime = _dbService.GetSetting(CursorKey(userId));
                if (string.IsNullOrEmpty(savedSyncTime))
                {
                    // 迁移兼容：老版本使用全局键，首次按新键读取不到时回退一次
                    savedSyncTime = _dbService.GetSetting("LastSyncTimeUtc");
                }
                if (!string.IsNullOrEmpty(savedSyncTime) && DateTime.TryParse(savedSyncTime, out var parsed))
                {
                    _lastSyncTimeUtc = parsed;
                }
            }

            // 用 PeriodicTimer 替代 System.Timers.Timer，正确处理 async
            _cts = new CancellationTokenSource();
            _syncTimer = new PeriodicTimer(TimeSpan.FromSeconds(SupabaseConfig.SyncIntervalSeconds));
            _syncLoopTask = RunSyncLoopAsync(_cts.Token);

            // 网络恢复时自动触发同步（保存引用以便 Stop 时取消订阅）
            _networkHandler = async (_, online) =>
            {
                if (online && _authService.IsLoggedIn)
                {
                    System.Diagnostics.Debug.WriteLine("[SyncService] Network restored, triggering sync");
                    try { await SyncAsync(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SyncService] Network restore sync failed: {ex.Message}"); }
                }
            };
            _network.ConnectivityChanged += _networkHandler;
        }

        /// <summary>同步游标的设置键（按用户隔离）</summary>
        private static string CursorKey(string userId) => $"LastSyncTimeUtc:{userId}";
        
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
            // 防重入：Interlocked 原子操作防止并发同步
            if (Interlocked.CompareExchange(ref _syncInProgress, 1, 0) != 0)
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
                
                // 3. 成长数据同步（XP 流水/番茄会话上传 + 用户档案合并；尽力而为，失败不影响主流程）
                try
                {
                    await SyncGrowthDataAsync();
                }
                catch (Exception growthEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Growth sync skipped: {growthEx.Message}");
                }

                // 4. 定期清理软删除记录（30天前的）
                _dbService.PurgeDeletedTasks(30);
                
                result.Success = true;
                LastSyncTime = DateTime.Now;
                
                // 保存同步时间到数据库（S8 修复：按用户隔离的游标键）
                _lastSyncTimeUtc = DateTime.UtcNow;
                var currentUserId = AuthService.Instance.CurrentUser?.Id;
                if (!string.IsNullOrEmpty(currentUserId))
                    _dbService.SetSetting(CursorKey(currentUserId), _lastSyncTimeUtc.Value.ToString("O"));
                
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
            finally
            {
                Interlocked.Exchange(ref _syncInProgress, 0);
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
                var taskMapping = new List<(int localId, SyncTask syncTask, string? expectedLocalUpdatedAt)>();

                foreach (var task in dirtyTasks)
                {
                    // M11 修复：SyncId 损坏时不再让整个上传流程卡死，视为无 SyncId 重新生成
                    Guid syncId;
                    if (string.IsNullOrEmpty(task.SyncId) || !Guid.TryParse(task.SyncId, out syncId))
                        syncId = Guid.NewGuid();

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
                    // S7 修复：记录上传时的 LocalUpdatedAt 快照，标记已同步时做乐观校验
                    taskMapping.Add((task.Id, syncTask, task.LocalUpdatedAt?.ToString("O")));
                }

                // 批量 upsert（一次 HTTP 请求）
                try
                {
                    await client.From<SyncTask>().Upsert(syncTasks);

                    // 全部成功，标记本地任务已同步
                    foreach (var (localId, syncTask, expected) in taskMapping)
                    {
                        _dbService.MarkTaskSynced(localId, syncTask.Id.ToString(), expected);
                    }

                    return syncTasks.Count;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Batch upload error: {ex.Message}");

                    // 批量失败时逐条重试（指数退避）
                    int uploaded = 0;
                    int failed = 0;
                    int retryDelay = 500; // 初始 500ms
                    foreach (var (localId, syncTask, expected) in taskMapping)
                    {
                        try
                        {
                            await client.From<SyncTask>().Upsert(syncTask);
                            _dbService.MarkTaskSynced(localId, syncTask.Id.ToString(), expected);
                            uploaded++;
                            retryDelay = 500; // 成功则重置
                        }
                        catch (Exception itemEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"Upload task {localId} error: {itemEx.Message}");
                            failed++;
                            // IsDirty 保持为1，下次同步会重试
                            await Task.Delay(retryDelay);
                            retryDelay = Math.Min(retryDelay * 2, 5000); // 最大 5 秒
                        }
                    }
                    
                    // 全部失败视为同步失败（让 SyncAsync 感知），部分成功则返回成功条数
                    if (failed > 0 && uploaded == 0)
                        throw new InvalidOperationException($"批量上传全部失败（{failed} 条），稍后重试");
                    return uploaded;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UploadLocalChanges error: {ex.Message}");
                throw;
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
                            LastSyncedAt = DateTime.UtcNow  // 与数据库 LastSyncedAt 存储格式（UTC）一致
                        };
                        
                        // LWW 冲突解决（S7 修复：基线从 LastSyncedAt 改为 LocalUpdatedAt。
                        // 原实现拿"上次同步时间"当本地编辑时间，本地根本没有编辑时间戳，
                        // 导致部分上传失败时几乎恒判"远程胜"，静默覆盖本地较新的修改）
                        var existing = _dbService.GetTaskBySyncId(remoteTask.Id.ToString());

                        if (existing != null && existing.IsDirty)
                        {
                            // 冲突：本地有未同步的修改 + 远程也有修改
                            var localEditTime = existing.LocalUpdatedAt ?? existing.LastSyncedAt ?? DateTime.MinValue;
                            if (remoteTask.UpdatedAt > localEditTime)
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
                throw; // 让 SyncAsync 感知下载失败，避免误报同步成功
            }
        }
        
        /// <summary>
        /// 成长数据同步（P5，尽力而为）：
        /// 1) 上传本地待同步的 XP 流水与番茄会话（IsDirty=1）；
        /// 2) 用户成长档案跨设备合并：按累计总经验"大者胜"。
        /// 流水以本机为准（跨设备 TaskId 不对应，避免重复合并）；等级一致性由 TotalXp 兜底。
        /// 需 Supabase 存在 xp_log / pomodoro_session / user_profile 表。
        /// </summary>
        private async Task SyncGrowthDataAsync()
        {
            var client = SupabaseClientService.Client;
            var userId = _authService.CurrentUser?.Id;
            if (string.IsNullOrEmpty(userId))
                return;

            // 1. 上传 XP 流水
            var dirtyXp = _dbService.GetDirtyXpLogs();
            if (dirtyXp.Count > 0)
            {
                var batch = dirtyXp.Select(x => new SyncXpLog
                {
                    UserId = userId,
                    Source = x.Source,
                    Amount = x.Amount,
                    TaskId = x.TaskId,
                    Date = x.Date,
                    CreatedAt = x.CreatedAt
                }).ToList();
                await client.From<SyncXpLog>().Upsert(batch);
                foreach (var x in dirtyXp)
                    _dbService.MarkXpLogSynced(x.Id);
            }

            // 2. 上传番茄会话
            var dirtyPomo = _dbService.GetDirtyPomodoroSessions();
            if (dirtyPomo.Count > 0)
            {
                var batch = dirtyPomo.Select(p => new SyncPomodoroSession
                {
                    UserId = userId,
                    TaskId = p.TaskId,
                    StartTime = p.StartTime,
                    EndTime = p.EndTime,
                    DurationMinutes = p.DurationMinutes,
                    Completed = p.Completed,
                    Date = p.Date
                }).ToList();
                await client.From<SyncPomodoroSession>().Upsert(batch);
                foreach (var p in dirtyPomo)
                    _dbService.MarkPomodoroSynced(p.Id);
            }

            // 3. 用户档案合并（TotalXp 大者胜）
            var local = _dbService.GetUserGrowth();
            var remoteList = await client.From<SyncUserProfile>()
                .Where(x => x.UserId == userId)
                .Get();
            // S5 修复：客户端按 UpdatedAt 取最新一行，避免多行时任意取值
            var remote = remoteList.Models
                .OrderByDescending(m => m.UpdatedAt)
                .FirstOrDefault();

            // S5 修复：上传时复用云端已有行的主键，保证 upsert 是"更新"而非"插入"
            var profileId = remote?.Id ?? Guid.NewGuid();

            if (remote == null)
            {
                // 云端无档案：上传本地
                await client.From<SyncUserProfile>().Upsert(new SyncUserProfile
                {
                    Id = profileId,
                    UserId = userId,
                    Level = local.Level,
                    Xp = local.Xp,
                    TotalXp = local.TotalXp,
                    ComboDays = local.ComboDays,
                    BestComboDays = local.BestComboDays,
                    Title = local.Title
                });
            }
            else if (remote.TotalXp > local.TotalXp)
            {
                // 云端更大：拉取合并到本地（按 TotalXp 重算等级）
                var (level, xp) = LevelService.DeriveFromTotal(remote.TotalXp);
                local.Level = level;
                local.Xp = xp;
                local.TotalXp = remote.TotalXp;
                local.ComboDays = Math.Max(local.ComboDays, remote.ComboDays);
                local.BestComboDays = Math.Max(local.BestComboDays, remote.BestComboDays);
                local.Title = LevelService.TitleForLevel(level);
                _dbService.SaveUserGrowth(local);
            }
            else if (local.TotalXp > remote.TotalXp)
            {
                // 本地更大：覆盖云端
                await client.From<SyncUserProfile>().Upsert(new SyncUserProfile
                {
                    Id = profileId,
                    UserId = userId,
                    Level = local.Level,
                    Xp = local.Xp,
                    TotalXp = local.TotalXp,
                    ComboDays = local.ComboDays,
                    BestComboDays = local.BestComboDays,
                    Title = local.Title
                });
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
        /// 停止同步服务（幂等；Stop 后可再次 InitializeAsync 重启）
        /// </summary>
        public void Stop()
        {
            try
            {
                _cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 已释放，忽略
            }

            _syncTimer?.Dispose();
            _cts?.Dispose();

            // 清理引用，允许再次初始化
            _syncTimer = null;
            _cts = null;
            _syncLoopTask = null;

            if (_networkHandler != null)
            {
                _network.ConnectivityChanged -= _networkHandler;
                _networkHandler = null;
            }

            // 复位防重入标记，避免停止后再启动时卡死
            Interlocked.Exchange(ref _syncInProgress, 0);
        }
    }
}
