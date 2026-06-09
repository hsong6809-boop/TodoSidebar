using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Supabase;
using TodoSidebar.Config;
using TodoSidebar.Models;

namespace TodoSidebar.Services
{
    /// <summary>
    /// 同步服务
    /// </summary>
    public class SyncService
    {
        private static SyncService? _instance;
        private static readonly object _lock = new object();
        
        private Timer? _syncTimer;
        private readonly Queue<SyncQueueItem> _offlineQueue = new Queue<SyncQueueItem>();
        private readonly object _queueLock = new object();
        private readonly DatabaseService _dbService = DatabaseService.Instance;
        private readonly AuthService _authService = AuthService.Instance;
        
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
        /// 初始化同步服务
        /// </summary>
        public async Task InitializeAsync()
        {
            await SupabaseClientService.InitializeAsync();
            
            // 启动定时同步
            _syncTimer = new Timer(SupabaseConfig.SyncIntervalSeconds * 1000);
            _syncTimer.Elapsed += async (s, e) => await SyncAsync();
            _syncTimer.AutoReset = true;
            _syncTimer.Start();
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
            
            SetStatus(SyncStatus.Syncing);
            
            try
            {
                var result = new SyncResult();
                
                // 1. 上传本地更改
                result.Uploaded = await UploadLocalChangesAsync();
                
                // 2. 下载远程更改
                result.Downloaded = await DownloadRemoteChangesAsync();
                
                // 3. 处理离线队列
                await ProcessOfflineQueueAsync();
                
                result.Success = true;
                LastSyncTime = DateTime.Now;
                
                SetStatus(SyncStatus.Idle);
                SyncCompleted?.Invoke(this, result);
                
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Sync error: {ex.Message}");
                SetStatus(SyncStatus.Error);
                return new SyncResult { Success = false, Error = ex.Message };
            }
        }
        
        /// <summary>
        /// 上传本地更改
        /// </summary>
        private async Task<int> UploadLocalChangesAsync()
        {
            try
            {
                // 1. 获取所有脏任务（本地修改但未同步）
                var dirtyTasks = _dbService.GetDirtyTasks();
                if (dirtyTasks.Count == 0)
                    return 0;
                
                var client = SupabaseClientService.Client;
                var userId = _authService.CurrentUser?.Id;
                if (string.IsNullOrEmpty(userId))
                    return 0;
                
                int uploaded = 0;
                
                foreach (var task in dirtyTasks)
                {
                    try
                    {
                        // 转换为 Supabase 模型
                        var syncTask = new SyncTask
                        {
                            Id = string.IsNullOrEmpty(task.SyncId) ? Guid.NewGuid() : Guid.Parse(task.SyncId),
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
                            IsDeleted = false
                        };
                        
                        // 上传到 Supabase (upsert)
                        await client.From<SyncTask>().Upsert(syncTask);
                        
                        // 标记本地任务已同步
                        _dbService.MarkTaskSynced(task.Id, syncTask.Id.ToString());
                        uploaded++;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Upload task {task.Id} error: {ex.Message}");
                        // 添加到离线队列
                        AddToOfflineQueue(new SyncQueueItem
                        {
                            TaskId = string.IsNullOrEmpty(task.SyncId) ? Guid.NewGuid() : Guid.Parse(task.SyncId),
                            Operation = "update",
                            TaskData = JsonConvert.SerializeObject(task),
                            LastError = ex.Message
                        });
                    }
                }
                
                return uploaded;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UploadLocalChanges error: {ex.Message}");
                return 0;
            }
        }
        
        /// <summary>
        /// 下载远程更改
        /// </summary>
        private async Task<int> DownloadRemoteChangesAsync()
        {
            try
            {
                var client = SupabaseClientService.Client;
                var userId = _authService.CurrentUser?.Id;
                if (string.IsNullOrEmpty(userId))
                    return 0;
                
                // 从 Supabase 获取当前用户的所有任务
                var response = await client.From<SyncTask>()
                    .Where(x => x.UserId == userId && !x.IsDeleted)
                    .Get();
                
                var remoteTasks = response.Models;
                if (remoteTasks == null || remoteTasks.Count == 0)
                    return 0;
                
                int downloaded = 0;
                
                foreach (var remoteTask in remoteTasks)
                {
                    try
                    {
                        // 转换为本地模型
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
                            IsDirty = false,
                            LastSyncedAt = DateTime.Now
                        };
                        
                        // 更新或插入本地数据库
                        _dbService.UpsertTaskFromRemote(localTask);
                        downloaded++;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Download task {remoteTask.Id} error: {ex.Message}");
                    }
                }
                
                return downloaded;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DownloadRemoteChanges error: {ex.Message}");
                return 0;
            }
        }
        
        /// <summary>
        /// 处理离线队列
        /// </summary>
        private async Task ProcessOfflineQueueAsync()
        {
            var itemsToProcess = new List<SyncQueueItem>();
            await Task.CompletedTask; // 占位符，实际操作是同步的
            
            // 取出队列中的所有项
            lock (_queueLock)
            {
                while (_offlineQueue.Count > 0)
                {
                    itemsToProcess.Add(_offlineQueue.Dequeue());
                }
            }
            
            if (itemsToProcess.Count == 0)
                return;
            
            var client = SupabaseClientService.Client;
            var userId = _authService.CurrentUser?.Id;
            if (string.IsNullOrEmpty(userId))
            {
                // 未登录，放回队列
                lock (_queueLock)
                {
                    foreach (var item in itemsToProcess)
                        _offlineQueue.Enqueue(item);
                }
                return;
            }
            
            foreach (var item in itemsToProcess)
            {
                try
                {
                    if (item.Operation == "update" || item.Operation == "create")
                    {
                        var task = JsonConvert.DeserializeObject<TaskItem>(item.TaskData ?? "{}");
                        if (task != null)
                        {
                            // 重新标记为脏，下次同步时会上传
                            task.IsDirty = true;
                            _dbService.UpdateTask(task);
                        }
                    }
                    else if (item.Operation == "delete" && item.TaskId != Guid.Empty)
                    {
                        // 软删除：标记为已删除
                        var existing = _dbService.GetTaskBySyncId(item.TaskId.ToString());
                        if (existing != null)
                        {
                            _dbService.DeleteTask(existing.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Process offline queue item error: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// 添加到离线队列
        /// </summary>
        public void AddToOfflineQueue(SyncQueueItem item)
        {
            lock (_queueLock)
            {
                if (_offlineQueue.Count < SupabaseConfig.MaxOfflineQueueSize)
                {
                    _offlineQueue.Enqueue(item);
                }
            }
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
            _syncTimer?.Stop();
            _syncTimer?.Dispose();
        }
    }
}
