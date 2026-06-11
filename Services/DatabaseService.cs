using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Data.Sqlite;
using TodoSidebar.Models;

namespace TodoSidebar.Services
{
    public partial class DatabaseService : IDatabaseService, IDisposable
    {
        private static DatabaseService? _instance;
        private static readonly object _lock = new object();
        
        public static DatabaseService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new DatabaseService();
                            _instance.Initialize();
                        }
                    }
                }
                return _instance;
            }
        }

        private readonly string _dbPath;
        private SqliteConnection? _connection;
        private readonly SemaphoreSlim _dbLock = new(1, 1);

        private DatabaseService()  // 改为私有构造函数
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appFolder = Path.Combine(appDataPath, "TodoSidebar");
            Directory.CreateDirectory(appFolder);
            _dbPath = Path.Combine(appFolder, "todo.db");
        }

        // 保留 Initialize 方法供首次调用
        public void Initialize()
        {
            if (_connection != null) return; // 已初始化
            try
            {
                _connection = new SqliteConnection($"Data Source={_dbPath}");
                _connection.Open();
                // 开启 WAL 模式，提升并发读写性能
                using (var walCmd = _connection.CreateCommand())
                {
                    walCmd.CommandText = "PRAGMA journal_mode=WAL;";
                    walCmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                // 数据库连接失败，尝试备份后重建
                System.Diagnostics.Debug.WriteLine($"数据库连接失败: {ex.Message}，将尝试备份后重建");

                try
                {
                    _connection?.Dispose();
                    if (File.Exists(_dbPath))
                    {
                        // 备份损坏的数据库文件，避免数据丢失
                        var backupPath = _dbPath + ".corrupted.bak";
                        try
                        {
                            File.Copy(_dbPath, backupPath, overwrite: true);
                            System.Diagnostics.Debug.WriteLine($"已备份损坏的数据库到: {backupPath}");
                        }
                        catch (Exception backupEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"备份损坏数据库失败: {backupEx.Message}");
                        }
                        File.Delete(_dbPath);
                    }
                    _connection = new SqliteConnection($"Data Source={_dbPath}");
                    _connection.Open();
                    using (var walCmd = _connection.CreateCommand())
                    {
                        walCmd.CommandText = "PRAGMA journal_mode=WAL;";
                        walCmd.ExecuteNonQuery();
                    }
                }
                catch (Exception ex2)
                {
                    throw new InvalidOperationException($"无法创建数据库: {ex2.Message}", ex2);
                }
            }

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Tasks (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Title TEXT NOT NULL,
                    Type INTEGER NOT NULL,
                    Priority INTEGER NOT NULL DEFAULT 1,
                    IsCompleted INTEGER NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL,
                    Deadline TEXT,
                    CompletedAt TEXT,
                    Description TEXT,
                    Tags TEXT,
                    SortOrder INTEGER DEFAULT 0,
                    EstimatedMinutes INTEGER,
                    ActualMinutes INTEGER,
                    SubTasksJson TEXT
                );
";
            cmd.ExecuteNonQuery();

            // 创建设置表
            using var settingsCmd = _connection.CreateCommand();
            settingsCmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Settings (
                    Key TEXT PRIMARY KEY,
                    Value TEXT NOT NULL
                );
            ";
            settingsCmd.ExecuteNonQuery();

            // 创建每日任务完成记录表（每天的完成状态独立）
            using var dailyCompCmd = _connection.CreateCommand();
            dailyCompCmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS DailyTaskCompletion (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    TaskId INTEGER NOT NULL,
                    Date TEXT NOT NULL,
                    UNIQUE(TaskId, Date)
                );
            ";
            dailyCompCmd.ExecuteNonQuery();

            // 检查并添加 Priority 列（如果不存在）
            try
            {
                using var checkCmd = _connection.CreateCommand();
                checkCmd.CommandText = "SELECT Priority FROM Tasks LIMIT 1";
                checkCmd.ExecuteScalar();
            }
            catch
            {
                // 列不存在，添加它
                using var alterCmd = _connection.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE Tasks ADD COLUMN Priority INTEGER NOT NULL DEFAULT 1";
                alterCmd.ExecuteNonQuery();
            }

            // 检查并添加新列（Description, Tags, SortOrder, EstimatedMinutes, ActualMinutes）
            MigrateDatabase();
        }

        private void MigrateDatabase()
        {
            var columnsToCheck = new Dictionary<string, string>
            {
                { "Description", "TEXT" },
                { "Tags", "TEXT" },
                { "SortOrder", "INTEGER DEFAULT 0" },
                { "EstimatedMinutes", "INTEGER" },
                { "ActualMinutes", "INTEGER" },
                { "SubTasksJson", "TEXT" },
                { "SyncId", "TEXT" },
                { "IsDirty", "INTEGER DEFAULT 1" },
                { "LastSyncedAt", "TEXT" },
                { "IsDeleted", "INTEGER DEFAULT 0" }
            };

            foreach (var column in columnsToCheck)
            {
                try
                {
                    using var checkCmd = _connection.CreateCommand();
                    checkCmd.CommandText = $"SELECT {column.Key} FROM Tasks LIMIT 1";
                    checkCmd.ExecuteScalar();
                }
                catch (Exception checkEx)
                {
                    try
                    {
                        using var alterCmd = _connection.CreateCommand();
                        alterCmd.CommandText = $"ALTER TABLE Tasks ADD COLUMN {column.Key} {column.Value}";
                        alterCmd.ExecuteNonQuery();
                    }
                    catch (Exception alterEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"ALTER TABLE ADD COLUMN {column.Key} 失败: {alterEx.Message}");
                    }
                }
            }
        }

        // Task CRUD
        public int InsertTask(TaskItem task)
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Tasks (Title, Type, Priority, IsCompleted, CreatedAt, Deadline, Description, Tags, SortOrder, EstimatedMinutes, ActualMinutes, SubTasksJson, IsDirty)
                VALUES (@title, @type, @priority, @completed, @createdAt, @deadline, @description, @tags, @sortOrder, @estimatedMinutes, @actualMinutes, @subTasksJson, 1);
                SELECT last_insert_rowid();
            ";
            cmd.Parameters.AddWithValue("@title", task.Title);
            cmd.Parameters.AddWithValue("@type", (int)task.Type);
            cmd.Parameters.AddWithValue("@priority", (int)task.Priority);
            cmd.Parameters.AddWithValue("@completed", task.IsCompleted ? 1 : 0);
            cmd.Parameters.AddWithValue("@createdAt", task.CreatedAt.ToString("O"));
            cmd.Parameters.AddWithValue("@deadline", task.Deadline?.ToString("O") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@description", task.Description ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@tags", task.Tags ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@sortOrder", task.SortOrder);
            cmd.Parameters.AddWithValue("@estimatedMinutes", task.EstimatedMinutes ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@actualMinutes", task.ActualMinutes ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@subTasksJson", task.SubTasksJson ?? (object)DBNull.Value);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public void UpdateTask(TaskItem task)
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = @"
                UPDATE Tasks SET
                    Title = @title,
                    Priority = @priority,
                    IsCompleted = @completed,
                    Deadline = @deadline,
                    CompletedAt = @completedAt,
                    Description = @description,
                    Tags = @tags,
                    SortOrder = @sortOrder,
                    EstimatedMinutes = @estimatedMinutes,
                    ActualMinutes = @actualMinutes,
                    SubTasksJson = @subTasksJson,
                    IsDirty = 1
                WHERE Id = @id
            ";
            cmd.Parameters.AddWithValue("@id", task.Id);
            cmd.Parameters.AddWithValue("@title", task.Title);
            cmd.Parameters.AddWithValue("@priority", (int)task.Priority);
            cmd.Parameters.AddWithValue("@completed", task.IsCompleted ? 1 : 0);
            cmd.Parameters.AddWithValue("@deadline", task.Deadline?.ToString("O") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@completedAt", task.CompletedAt?.ToString("O") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@description", task.Description ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@tags", task.Tags ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@sortOrder", task.SortOrder);
            cmd.Parameters.AddWithValue("@estimatedMinutes", task.EstimatedMinutes ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@actualMinutes", task.ActualMinutes ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@subTasksJson", task.SubTasksJson ?? (object)DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        public void DeleteTask(int id)
        {
            // 软删除：标记 IsDeleted + IsDirty，同步时会上传到云端
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "UPDATE Tasks SET IsDeleted = 1, IsDirty = 1 WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// 通过 ID 获取单个任务
        /// </summary>
        public TaskItem? GetTaskById(int taskId)
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT * FROM Tasks WHERE Id = @id AND IsDeleted = 0";
            cmd.Parameters.AddWithValue("@id", taskId);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
                return ReadTask(reader);
            return null;
        }

        /// <summary>
        /// 彻底删除已软删除且已同步的任务（定期清理用）
        /// </summary>
        public void PurgeDeletedTasks(int daysOld = 30)
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = @"
                DELETE FROM Tasks
                WHERE IsDeleted = 1
                  AND IsDirty = 0
                  AND LastSyncedAt IS NOT NULL
                  AND LastSyncedAt < datetime('now', '-' || @daysOld || ' days')";
            cmd.Parameters.AddWithValue("@daysOld", daysOld.ToString());
            cmd.ExecuteNonQuery();
        }

        public List<TaskItem> GetTasks(TaskType? type = null, bool? completed = null)
        {
            var tasks = new List<TaskItem>();
            using var cmd = _connection!.CreateCommand();
            
            var sql = "SELECT * FROM Tasks WHERE IsDeleted = 0";
            if (type.HasValue)
            {
                sql += " AND Type = @type";
                cmd.Parameters.AddWithValue("@type", (int)type.Value);
            }
            if (completed.HasValue)
            {
                sql += " AND IsCompleted = @completed";
                cmd.Parameters.AddWithValue("@completed", completed.Value ? 1 : 0);
            }
            sql += " ORDER BY CreatedAt DESC";
            
            cmd.CommandText = sql;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                tasks.Add(ReadTask(reader));
            }
            return tasks;
        }

        public List<TaskItem> GetCompletedTasks(DateTime? fromDate = null, DateTime? toDate = null)
        {
            var tasks = new List<TaskItem>();
            using var cmd = _connection!.CreateCommand();
            
            var sql = "SELECT * FROM Tasks WHERE IsCompleted = 1 AND IsDeleted = 0";
            if (fromDate.HasValue)
            {
                sql += " AND CompletedAt >= @fromDate";
                cmd.Parameters.AddWithValue("@fromDate", fromDate.Value.ToString("O"));
            }
            if (toDate.HasValue)
            {
                sql += " AND CompletedAt <= @toDate";
                cmd.Parameters.AddWithValue("@toDate", toDate.Value.ToString("O"));
            }
            sql += " ORDER BY CompletedAt DESC";
            
            cmd.CommandText = sql;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                tasks.Add(ReadTask(reader));
            }
            return tasks;
        }

        private TaskItem ReadTask(SqliteDataReader reader)
        {
            return new TaskItem
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                Title = reader.GetString(reader.GetOrdinal("Title")),
                Type = (TaskType)reader.GetInt32(reader.GetOrdinal("Type")),
                Priority = reader.IsDBNull(reader.GetOrdinal("Priority")) ? TaskPriority.Medium : (TaskPriority)reader.GetInt32(reader.GetOrdinal("Priority")),
                IsCompleted = reader.GetInt32(reader.GetOrdinal("IsCompleted")) == 1,
                CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
                Deadline = reader.IsDBNull(reader.GetOrdinal("Deadline")) ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("Deadline"))),
                CompletedAt = reader.IsDBNull(reader.GetOrdinal("CompletedAt")) ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("CompletedAt"))),
                Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString(reader.GetOrdinal("Description")),
                Tags = reader.IsDBNull(reader.GetOrdinal("Tags")) ? null : reader.GetString(reader.GetOrdinal("Tags")),
                SortOrder = reader.IsDBNull(reader.GetOrdinal("SortOrder")) ? 0 : reader.GetInt32(reader.GetOrdinal("SortOrder")),
                EstimatedMinutes = reader.IsDBNull(reader.GetOrdinal("EstimatedMinutes")) ? null : reader.GetInt32(reader.GetOrdinal("EstimatedMinutes")),
                ActualMinutes = reader.IsDBNull(reader.GetOrdinal("ActualMinutes")) ? null : reader.GetInt32(reader.GetOrdinal("ActualMinutes")),
                SubTasksJson = reader.IsDBNull(reader.GetOrdinal("SubTasksJson")) ? null : reader.GetString(reader.GetOrdinal("SubTasksJson")),
                SyncId = reader.IsDBNull(reader.GetOrdinal("SyncId")) ? null : reader.GetString(reader.GetOrdinal("SyncId")),
                IsDirty = reader.IsDBNull(reader.GetOrdinal("IsDirty")) ? true : reader.GetInt32(reader.GetOrdinal("IsDirty")) == 1,
                LastSyncedAt = reader.IsDBNull(reader.GetOrdinal("LastSyncedAt")) ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("LastSyncedAt"))),
                IsDeleted = reader.IsDBNull(reader.GetOrdinal("IsDeleted")) ? false : reader.GetInt32(reader.GetOrdinal("IsDeleted")) == 1
            };
        }

        // Settings
        public string? GetSetting(string key)
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT Value FROM Settings WHERE Key = @key";
            cmd.Parameters.AddWithValue("@key", key);
            var result = cmd.ExecuteScalar();
            return result?.ToString();
        }

        public void SetSetting(string key, string value)
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO Settings (Key, Value) VALUES (@key, @value)
            ";
            cmd.Parameters.AddWithValue("@key", key);
            cmd.Parameters.AddWithValue("@value", value);
            cmd.ExecuteNonQuery();
        }

        // ========== 每日任务完成记录 ==========

        /// <summary>
        /// 标记每日任务在指定日期完成
        /// </summary>
        public void MarkDailyTaskCompleted(int taskId, string date)
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = @"
                INSERT OR IGNORE INTO DailyTaskCompletion (TaskId, Date) VALUES (@taskId, @date)
            ";
            cmd.Parameters.AddWithValue("@taskId", taskId);
            cmd.Parameters.AddWithValue("@date", date);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// 取消每日任务在指定日期的完成状态
        /// </summary>
        public void UnmarkDailyTaskCompleted(int taskId, string date)
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "DELETE FROM DailyTaskCompletion WHERE TaskId = @taskId AND Date = @date";
            cmd.Parameters.AddWithValue("@taskId", taskId);
            cmd.Parameters.AddWithValue("@date", date);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// 获取今天已完成的每日任务ID集合
        /// </summary>
        public HashSet<int> GetTodayCompletedDailyTaskIds()
        {
            var ids = new HashSet<int>();
            var today = DateTime.Today.ToString("yyyy-MM-dd");
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT TaskId FROM DailyTaskCompletion WHERE Date = @date";
            cmd.Parameters.AddWithValue("@date", today);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                ids.Add(reader.GetInt32(0));
            return ids;
        }

        /// <summary>
        /// 获取今天已完成的每日任务（完整对象）
        /// </summary>
        public List<TaskItem> GetTodayCompletedDailyTasks()
        {
            var today = DateTime.Today.ToString("yyyy-MM-dd");
            var tasks = new List<TaskItem>();
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = @"
                SELECT t.* FROM Tasks t
                INNER JOIN DailyTaskCompletion d ON t.Id = d.TaskId
                WHERE d.Date = @date AND t.IsDeleted = 0
                ORDER BY d.Id DESC
            ";
            cmd.Parameters.AddWithValue("@date", today);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                tasks.Add(ReadTask(reader));
            return tasks;
        }

        /// <summary>
        /// 获取每日任务在指定日期是否完成
        /// </summary>
        public bool IsDailyTaskCompletedOnDate(int taskId, string date)
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM DailyTaskCompletion WHERE TaskId = @taskId AND Date = @date";
            cmd.Parameters.AddWithValue("@taskId", taskId);
            cmd.Parameters.AddWithValue("@date", date);
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }

        /// <summary>
        /// 标记任务为脏（需要同步），不修改其他字段
        /// </summary>
        public void MarkTaskDirty(int taskId)
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "UPDATE Tasks SET IsDirty = 1 WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", taskId);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// 获取最近 N 天的每日完成记录（用于统计）
        /// 返回每个日期对应的已完成任务 ID 集合
        /// </summary>
        public Dictionary<string, HashSet<int>> GetDailyCompletionRecords(int days)
        {
            var result = new Dictionary<string, HashSet<int>>();
            var startDate = DateTime.Today.AddDays(-(days - 1)).ToString("yyyy-MM-dd");
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT Date, TaskId FROM DailyTaskCompletion WHERE Date >= @startDate";
            cmd.Parameters.AddWithValue("@startDate", startDate);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var date = reader.GetString(0);
                var taskId = reader.GetInt32(1);
                if (!result.ContainsKey(date))
                    result[date] = new HashSet<int>();
                result[date].Add(taskId);
            }
            return result;
        }

        /// <summary>
        /// 获取每日任务总数（用于统计）
        /// </summary>
        public int GetDailyTaskCount()
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Tasks WHERE Type = @type AND IsDeleted = 0";
            cmd.Parameters.AddWithValue("@type", (int)TaskType.Daily);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        // 搜索任务
        public List<TaskItem> SearchTasks(string keyword, TaskType? type = null, TaskPriority? priority = null)
        {
            var tasks = new List<TaskItem>();
            using var cmd = _connection!.CreateCommand();
            
            var sql = "SELECT * FROM Tasks WHERE IsDeleted = 0 AND (Title LIKE @keyword OR Description LIKE @keyword OR Tags LIKE @keyword)";
            cmd.Parameters.AddWithValue("@keyword", $"%{keyword}%");
            
            if (type.HasValue)
            {
                sql += " AND Type = @type";
                cmd.Parameters.AddWithValue("@type", (int)type.Value);
            }
            
            if (priority.HasValue)
            {
                sql += " AND Priority = @priority";
                cmd.Parameters.AddWithValue("@priority", (int)priority.Value);
            }
            
            sql += " ORDER BY CreatedAt DESC";
            
            cmd.CommandText = sql;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                tasks.Add(ReadTask(reader));
            }
            return tasks;
        }

        // 获取所有任务（用于导出）
        public List<TaskItem> GetTasks()
        {
            var tasks = new List<TaskItem>();
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT * FROM Tasks WHERE IsDeleted = 0 ORDER BY SortOrder, CreatedAt DESC";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                tasks.Add(ReadTask(reader));
            }
            return tasks;
        }

        // 批量更新任务排序
        public void UpdateTaskOrder(List<(int id, int order)> orders)
        {
            using var transaction = _connection!.BeginTransaction();
            try
            {
                foreach (var (id, order) in orders)
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = "UPDATE Tasks SET SortOrder = @order WHERE Id = @id";
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@order", order);
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        // ========== 同步相关方法 ==========
        
        /// <summary>
        /// 获取所有需要同步的任务（IsDirty = 1）
        /// </summary>
        public List<TaskItem> GetDirtyTasks()
        {
            return ExecuteLocked(() =>
            {
                var tasks = new List<TaskItem>();
                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = "SELECT * FROM Tasks WHERE IsDirty = 1";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    tasks.Add(ReadTask(reader));
                }
                return tasks;
            });
        }

        /// <summary>
        /// 标记任务已同步
        /// </summary>
        public void MarkTaskSynced(int localId, string syncId)
        {
            ExecuteLocked(() =>
            {
                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = @"
                    UPDATE Tasks SET 
                        SyncId = @syncId, 
                        IsDirty = 0, 
                        LastSyncedAt = @syncedAt 
                    WHERE Id = @id
                ";
                cmd.Parameters.AddWithValue("@id", localId);
                cmd.Parameters.AddWithValue("@syncId", syncId);
                cmd.Parameters.AddWithValue("@syncedAt", DateTime.UtcNow.ToString("O"));
                cmd.ExecuteNonQuery();
            });
        }

        /// <summary>
        /// 通过 SyncId 获取本地任务
        /// </summary>
        public TaskItem? GetTaskBySyncId(string syncId)
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT * FROM Tasks WHERE SyncId = @syncId";
            cmd.Parameters.AddWithValue("@syncId", syncId);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return ReadTask(reader);
            }
            return null;
        }

        /// <summary>
        /// 通过 SyncId 更新本地任务（来自远程同步）
        /// </summary>
        public void UpsertTaskFromRemote(TaskItem task)
        {
            ExecuteLocked(() =>
            {
                var existing = GetTaskBySyncId(task.SyncId!);
            if (existing != null)
            {
                // 更新现有任务
                task.Id = existing.Id;
                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = @"
                    UPDATE Tasks SET
                        Title = @title,
                        Type = @type,
                        Priority = @priority,
                        IsCompleted = @completed,
                        CreatedAt = @createdAt,
                        Deadline = @deadline,
                        CompletedAt = @completedAt,
                        Description = @description,
                        Tags = @tags,
                        SortOrder = @sortOrder,
                        SubTasksJson = @subTasksJson,
                        IsDeleted = @isDeleted,
                        IsDirty = 0,
                        LastSyncedAt = @syncedAt
                    WHERE SyncId = @syncId
                ";
                cmd.Parameters.AddWithValue("@syncId", task.SyncId);
                cmd.Parameters.AddWithValue("@title", task.Title);
                cmd.Parameters.AddWithValue("@type", (int)task.Type);
                cmd.Parameters.AddWithValue("@priority", (int)task.Priority);
                cmd.Parameters.AddWithValue("@completed", task.IsCompleted ? 1 : 0);
                cmd.Parameters.AddWithValue("@createdAt", task.CreatedAt.ToString("O"));
                cmd.Parameters.AddWithValue("@deadline", task.Deadline?.ToString("O") ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@completedAt", task.CompletedAt?.ToString("O") ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@description", task.Description ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@tags", task.Tags ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@sortOrder", task.SortOrder);
                cmd.Parameters.AddWithValue("@subTasksJson", task.SubTasksJson ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@isDeleted", task.IsDeleted ? 1 : 0);
                cmd.Parameters.AddWithValue("@syncedAt", DateTime.UtcNow.ToString("O"));
                cmd.ExecuteNonQuery();
            }
            else
            {
                // 插入新任务
                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO Tasks (Title, Type, Priority, IsCompleted, CreatedAt, Deadline, CompletedAt, Description, Tags, SortOrder, SubTasksJson, SyncId, IsDirty, LastSyncedAt, IsDeleted)
                    VALUES (@title, @type, @priority, @completed, @createdAt, @deadline, @completedAt, @description, @tags, @sortOrder, @subTasksJson, @syncId, 0, @syncedAt, @isDeleted)
                ";
                cmd.Parameters.AddWithValue("@title", task.Title);
                cmd.Parameters.AddWithValue("@type", (int)task.Type);
                cmd.Parameters.AddWithValue("@priority", (int)task.Priority);
                cmd.Parameters.AddWithValue("@completed", task.IsCompleted ? 1 : 0);
                cmd.Parameters.AddWithValue("@createdAt", task.CreatedAt.ToString("O"));
                cmd.Parameters.AddWithValue("@deadline", task.Deadline?.ToString("O") ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@completedAt", task.CompletedAt?.ToString("O") ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@description", task.Description ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@tags", task.Tags ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@sortOrder", task.SortOrder);
                cmd.Parameters.AddWithValue("@subTasksJson", task.SubTasksJson ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@syncId", task.SyncId);
                cmd.Parameters.AddWithValue("@isDeleted", task.IsDeleted ? 1 : 0);
                cmd.Parameters.AddWithValue("@syncedAt", DateTime.UtcNow.ToString("O"));
                cmd.ExecuteNonQuery();
            }
            });
        }

        public void Dispose()
        {
            _dbLock?.Dispose();
            _connection?.Dispose();
        }

        /// <summary>
        /// 在数据库锁保护下执行操作，防止多线程并发访问
        /// </summary>
        private T ExecuteLocked<T>(Func<T> action)
        {
            _dbLock.Wait();
            try { return action(); }
            finally { _dbLock.Release(); }
        }

        private void ExecuteLocked(Action action)
        {
            _dbLock.Wait();
            try { action(); }
            finally { _dbLock.Release(); }
        }
    }
}
