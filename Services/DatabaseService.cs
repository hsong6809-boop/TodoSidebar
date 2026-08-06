using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
            // 测试环境支持：环境变量指定数据库路径，避免污染真实用户数据
            var testDbOverride = Environment.GetEnvironmentVariable("TODOSIDEBAR_TEST_DB");
            if (!string.IsNullOrEmpty(testDbOverride))
            {
                _dbPath = testDbOverride;
                var dir = Path.GetDirectoryName(testDbOverride);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                return;
            }

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

            using var cmd = _connection!.CreateCommand();
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
            using var settingsCmd = _connection!.CreateCommand();
            settingsCmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Settings (
                    Key TEXT PRIMARY KEY,
                    Value TEXT NOT NULL
                );
            ";
            settingsCmd.ExecuteNonQuery();

            // 创建每日任务完成记录表（每天的完成状态独立）
            using var dailyCompCmd = _connection!.CreateCommand();
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
                using var checkCmd = _connection!.CreateCommand();
                checkCmd.CommandText = "SELECT Priority FROM Tasks LIMIT 1";
                checkCmd.ExecuteScalar();
            }
            catch
            {
                // 列不存在，添加它
                using var alterCmd = _connection!.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE Tasks ADD COLUMN Priority INTEGER NOT NULL DEFAULT 1";
                alterCmd.ExecuteNonQuery();
            }

            // 检查并添加新列（Description, Tags, SortOrder, EstimatedMinutes, ActualMinutes）
            MigrateDatabase();

            // ===== 升级系统表 =====
            CreateGrowthTables();
            EnsureGrowthSyncColumns();
        }

        private void CreateGrowthTables()
        {
            var connection = _connection
                ?? throw new InvalidOperationException("数据库未初始化，无法创建升级系统表");

            using var profileCmd = connection.CreateCommand();
            profileCmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS UserProfile (
                    Id INTEGER PRIMARY KEY CHECK (Id = 1),
                    Level INTEGER NOT NULL DEFAULT 1,
                    Xp INTEGER NOT NULL DEFAULT 0,
                    TotalXp INTEGER NOT NULL DEFAULT 0,
                    ComboDays INTEGER NOT NULL DEFAULT 0,
                    BestComboDays INTEGER NOT NULL DEFAULT 0,
                    Title TEXT NOT NULL DEFAULT '初出茅庐',
                    LastXpDate TEXT
                );
            ";
            profileCmd.ExecuteNonQuery();

            using var xpLogCmd = connection.CreateCommand();
            xpLogCmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS XpLog (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Source TEXT NOT NULL,
                    Amount INTEGER NOT NULL,
                    TaskId INTEGER,
                    Date TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL
                );
            ";
            xpLogCmd.ExecuteNonQuery();

            using var achCmd = connection.CreateCommand();
            achCmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS AchievementUnlocks (
                    AchievementId TEXT PRIMARY KEY,
                    UnlockedAt TEXT NOT NULL
                );
            ";
            achCmd.ExecuteNonQuery();

            using var pomoCmd = connection.CreateCommand();
            pomoCmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS PomodoroSession (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    TaskId INTEGER,
                    StartTime TEXT NOT NULL,
                    EndTime TEXT,
                    DurationMinutes INTEGER NOT NULL,
                    Completed INTEGER NOT NULL DEFAULT 0,
                    Date TEXT NOT NULL
                );
            ";
            pomoCmd.ExecuteNonQuery();

            using var challengeCmd = connection.CreateCommand();
            challengeCmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS DailyChallenge (
                    Date TEXT NOT NULL,
                    ChallengeId TEXT NOT NULL,
                    Progress INTEGER NOT NULL DEFAULT 0,
                    Target INTEGER NOT NULL,
                    Completed INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY (Date, ChallengeId)
                );
            ";
            challengeCmd.ExecuteNonQuery();
        }

        private void MigrateDatabase()
        {
            var connection = _connection
                ?? throw new InvalidOperationException("数据库未初始化，无法执行迁移");
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
                    using var checkCmd = _connection!.CreateCommand();
                    checkCmd.CommandText = $"SELECT {column.Key} FROM Tasks LIMIT 1";
                    checkCmd.ExecuteScalar();
                }
                catch
                {
                    try
                    {
                        using var alterCmd = _connection!.CreateCommand();
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

        // ==================== 任务 CRUD（全部加锁） ====================

        public int InsertTask(TaskItem task) => ExecuteLocked(() => InsertTaskCore(task));

        private int InsertTaskCore(TaskItem task)
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

        /// <summary>
        /// 插入导入/恢复的任务，保留同步字段（SyncId/IsDirty/IsDeleted/LastSyncedAt），
        /// 避免恢复后与云端产生重复任务。
        /// </summary>
        public int InsertImportedTask(TaskItem task) => ExecuteLocked(() =>
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Tasks (Title, Type, Priority, IsCompleted, CreatedAt, Deadline, CompletedAt, Description, Tags, SortOrder, EstimatedMinutes, ActualMinutes, SubTasksJson, SyncId, IsDirty, LastSyncedAt, IsDeleted)
                VALUES (@title, @type, @priority, @completed, @createdAt, @deadline, @completedAt, @description, @tags, @sortOrder, @estimatedMinutes, @actualMinutes, @subTasksJson, @syncId, @isDirty, @lastSyncedAt, @isDeleted);
                SELECT last_insert_rowid();
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
            cmd.Parameters.AddWithValue("@estimatedMinutes", task.EstimatedMinutes ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@actualMinutes", task.ActualMinutes ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@subTasksJson", task.SubTasksJson ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@syncId", task.SyncId ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@isDirty", task.IsDirty ? 1 : 0);
            cmd.Parameters.AddWithValue("@lastSyncedAt", task.LastSyncedAt?.ToString("O") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@isDeleted", task.IsDeleted ? 1 : 0);
            return Convert.ToInt32(cmd.ExecuteScalar());
        });

        public void UpdateTask(TaskItem task) => ExecuteLocked(() =>
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
        });

        public void DeleteTask(int id) => ExecuteLocked(() =>
        {
            // 软删除：标记 IsDeleted + IsDirty，同步时会上传到云端
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "UPDATE Tasks SET IsDeleted = 1, IsDirty = 1 WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        });

        /// <summary>
        /// 通过 ID 获取单个任务
        /// </summary>
        public TaskItem? GetTaskById(int taskId) => ExecuteLocked(() =>
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT * FROM Tasks WHERE Id = @id AND IsDeleted = 0";
            cmd.Parameters.AddWithValue("@id", taskId);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
                return ReadTask(reader);
            return null;
        });

        /// <summary>
        /// 彻底删除已软删除且已同步的任务（定期清理用）
        /// </summary>
        public void PurgeDeletedTasks(int daysOld = 30) => ExecuteLocked(() =>
        {
            // LastSyncedAt 存储为 ISO-8601 "O" 格式（UTC），用同格式字符串比较，避免 SQLite datetime() 格式不匹配
            var cutoff = DateTime.UtcNow.AddDays(-daysOld).ToString("O");
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = @"
                DELETE FROM Tasks
                WHERE IsDeleted = 1
                  AND IsDirty = 0
                  AND LastSyncedAt IS NOT NULL
                  AND LastSyncedAt < @cutoff";
            cmd.Parameters.AddWithValue("@cutoff", cutoff);
            cmd.ExecuteNonQuery();
        });

        public List<TaskItem> GetTasks(TaskType? type = null, bool? completed = null) => ExecuteLocked(() =>
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
        });

        public List<TaskItem> GetCompletedTasks(DateTime? fromDate = null, DateTime? toDate = null) => ExecuteLocked(() =>
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
        });

        private TaskItem ReadTask(SqliteDataReader reader)
        {
            return new TaskItem
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                Title = reader.GetString(reader.GetOrdinal("Title")),
                Type = (TaskType)reader.GetInt32(reader.GetOrdinal("Type")),
                Priority = reader.IsDBNull(reader.GetOrdinal("Priority")) ? TaskPriority.Medium : (TaskPriority)reader.GetInt32(reader.GetOrdinal("Priority")),
                IsCompleted = reader.GetInt32(reader.GetOrdinal("IsCompleted")) == 1,
                CreatedAt = ReadDateTime(reader, "CreatedAt") ?? DateTime.Now,
                Deadline = ReadDateTime(reader, "Deadline"),
                CompletedAt = ReadDateTime(reader, "CompletedAt"),
                Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString(reader.GetOrdinal("Description")),
                Tags = reader.IsDBNull(reader.GetOrdinal("Tags")) ? null : reader.GetString(reader.GetOrdinal("Tags")),
                SortOrder = reader.IsDBNull(reader.GetOrdinal("SortOrder")) ? 0 : reader.GetInt32(reader.GetOrdinal("SortOrder")),
                EstimatedMinutes = reader.IsDBNull(reader.GetOrdinal("EstimatedMinutes")) ? null : reader.GetInt32(reader.GetOrdinal("EstimatedMinutes")),
                ActualMinutes = reader.IsDBNull(reader.GetOrdinal("ActualMinutes")) ? null : reader.GetInt32(reader.GetOrdinal("ActualMinutes")),
                SubTasksJson = reader.IsDBNull(reader.GetOrdinal("SubTasksJson")) ? null : reader.GetString(reader.GetOrdinal("SubTasksJson")),
                SyncId = reader.IsDBNull(reader.GetOrdinal("SyncId")) ? null : reader.GetString(reader.GetOrdinal("SyncId")),
                IsDirty = reader.IsDBNull(reader.GetOrdinal("IsDirty")) ? true : reader.GetInt32(reader.GetOrdinal("IsDirty")) == 1,
                LastSyncedAt = ReadDateTime(reader, "LastSyncedAt"),
                IsDeleted = reader.IsDBNull(reader.GetOrdinal("IsDeleted")) ? false : reader.GetInt32(reader.GetOrdinal("IsDeleted")) == 1
            };
        }

        /// <summary>
        /// 读取日期时间列，保留 RoundtripKind 语义（UTC 数据解析为 UTC Kind，本地数据解析为 Local Kind），
        /// 避免跨时区比较错位。
        /// </summary>
        private static DateTime? ReadDateTime(SqliteDataReader reader, string columnName)
        {
            if (reader.IsDBNull(reader.GetOrdinal(columnName)))
                return null;
            var raw = reader.GetString(reader.GetOrdinal(columnName));
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result))
                return result;
            return null;
        }

        // ==================== 设置 ====================

        public string? GetSetting(string key) => ExecuteLocked(() =>
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT Value FROM Settings WHERE Key = @key";
            cmd.Parameters.AddWithValue("@key", key);
            var result = cmd.ExecuteScalar();
            return result?.ToString();
        });

        public void SetSetting(string key, string value) => ExecuteLocked(() =>
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO Settings (Key, Value) VALUES (@key, @value)
            ";
            cmd.Parameters.AddWithValue("@key", key);
            cmd.Parameters.AddWithValue("@value", value);
            cmd.ExecuteNonQuery();
        });

        // ========== 每日任务完成记录（全部加锁） ==========

        /// <summary>
        /// 标记每日任务在指定日期完成
        /// </summary>
        public void MarkDailyTaskCompleted(int taskId, string date) => ExecuteLocked(() =>
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = @"
                INSERT OR IGNORE INTO DailyTaskCompletion (TaskId, Date) VALUES (@taskId, @date)
            ";
            cmd.Parameters.AddWithValue("@taskId", taskId);
            cmd.Parameters.AddWithValue("@date", date);
            cmd.ExecuteNonQuery();
        });

        /// <summary>
        /// 取消每日任务在指定日期的完成状态
        /// </summary>
        public void UnmarkDailyTaskCompleted(int taskId, string date) => ExecuteLocked(() =>
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "DELETE FROM DailyTaskCompletion WHERE TaskId = @taskId AND Date = @date";
            cmd.Parameters.AddWithValue("@taskId", taskId);
            cmd.Parameters.AddWithValue("@date", date);
            cmd.ExecuteNonQuery();
        });

        /// <summary>
        /// 获取今天已完成的每日任务ID集合
        /// </summary>
        public HashSet<int> GetTodayCompletedDailyTaskIds() => ExecuteLocked(() =>
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
        });

        /// <summary>
        /// 获取今天已完成的每日任务（完整对象）
        /// </summary>
        public List<TaskItem> GetTodayCompletedDailyTasks() => ExecuteLocked(() =>
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
        });

        /// <summary>
        /// 获取每日任务在指定日期是否完成
        /// </summary>
        public bool IsDailyTaskCompletedOnDate(int taskId, string date) => ExecuteLocked(() =>
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM DailyTaskCompletion WHERE TaskId = @taskId AND Date = @date";
            cmd.Parameters.AddWithValue("@taskId", taskId);
            cmd.Parameters.AddWithValue("@date", date);
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        });

        /// <summary>
        /// 标记任务为脏（需要同步），不修改其他字段
        /// </summary>
        public void MarkTaskDirty(int taskId) => ExecuteLocked(() =>
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "UPDATE Tasks SET IsDirty = 1 WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", taskId);
            cmd.ExecuteNonQuery();
        });

        /// <summary>
        /// 获取最近 N 天的每日完成记录（用于统计）
        /// 返回每个日期对应的已完成任务 ID 集合
        /// </summary>
        public Dictionary<string, HashSet<int>> GetDailyCompletionRecords(int days) => ExecuteLocked(() =>
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
        });

        /// <summary>
        /// 获取每日任务总数（用于统计）
        /// </summary>
        public int GetDailyTaskCount() => ExecuteLocked(() =>
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Tasks WHERE Type = @type AND IsDeleted = 0";
            cmd.Parameters.AddWithValue("@type", (int)TaskType.Daily);
            return Convert.ToInt32(cmd.ExecuteScalar());
        });

        // ==================== 搜索 ====================

        public List<TaskItem> SearchTasks(string keyword, TaskType? type = null, TaskPriority? priority = null) => ExecuteLocked(() =>
        {
            var tasks = new List<TaskItem>();
            using var cmd = _connection!.CreateCommand();
            
            var sql = "SELECT * FROM Tasks WHERE IsDeleted = 0 AND (Title LIKE @keyword ESCAPE '\\' OR Description LIKE @keyword ESCAPE '\\' OR Tags LIKE @keyword ESCAPE '\\')";
            // 转义 LIKE 通配符，避免用户输入 % _ 扩大匹配范围
            var escaped = keyword.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            cmd.Parameters.AddWithValue("@keyword", $"%{escaped}%");
            
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
        });

        // 获取所有任务（用于导出）
        public List<TaskItem> GetTasks() => ExecuteLocked(() =>
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
        });

        // 批量更新任务排序
        public void UpdateTaskOrder(List<(int id, int order)> orders) => ExecuteLocked(() =>
        {
            using var transaction = _connection!.BeginTransaction();
            try
            {
                foreach (var (id, order) in orders)
                {
                    using var cmd = _connection!.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = "UPDATE Tasks SET SortOrder = @order WHERE Id = @id";
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@order", order);
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); } catch { /* 已回滚或连接异常 */ }
                throw;
            }
        });

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
        public TaskItem? GetTaskBySyncId(string syncId) => ExecuteLocked(() => GetTaskBySyncIdCore(syncId));

        private TaskItem? GetTaskBySyncIdCore(string syncId)
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
                var existing = GetTaskBySyncIdCore(task.SyncId!);
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

        /// <summary>
        /// 原子替换全部任务（备份恢复用）：单事务内软删现有任务 + 插入恢复的任务。
        /// 任一步失败整体回滚，避免"先删光再导入失败"导致数据丢失。
        /// </summary>
        public void ReplaceAllTasks(List<TaskItem> tasks) => ExecuteLocked(() =>
        {
            using var transaction = _connection!.BeginTransaction();
            try
            {
                // 1. 软删全部现有任务
                using (var deleteCmd = _connection.CreateCommand())
                {
                    deleteCmd.Transaction = transaction;
                    deleteCmd.CommandText = "UPDATE Tasks SET IsDeleted = 1, IsDirty = 1";
                    deleteCmd.ExecuteNonQuery();
                }

                // 2. 插入恢复的任务（保留 SyncId/IsDirty，避免云端重复）
                foreach (var task in tasks)
                {
                    using var cmd = _connection!.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT INTO Tasks (Title, Type, Priority, IsCompleted, CreatedAt, Deadline, CompletedAt, Description, Tags, SortOrder, EstimatedMinutes, ActualMinutes, SubTasksJson, SyncId, IsDirty, LastSyncedAt, IsDeleted)
                        VALUES (@title, @type, @priority, @completed, @createdAt, @deadline, @completedAt, @description, @tags, @sortOrder, @estimatedMinutes, @actualMinutes, @subTasksJson, @syncId, @isDirty, @lastSyncedAt, @isDeleted)
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
                    cmd.Parameters.AddWithValue("@estimatedMinutes", task.EstimatedMinutes ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@actualMinutes", task.ActualMinutes ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@subTasksJson", task.SubTasksJson ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@syncId", task.SyncId ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@isDirty", task.IsDirty ? 1 : 0);
                    cmd.Parameters.AddWithValue("@lastSyncedAt", task.LastSyncedAt?.ToString("O") ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@isDeleted", task.IsDeleted ? 1 : 0);
                    cmd.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); } catch { /* 已回滚或连接异常 */ }
                throw;
            }
        });

        // ==================== 升级系统数据访问 ====================

        /// <summary>
        /// 获取用户成长档案；不存在则创建默认行（Lv.1）。
        /// </summary>
        public UserGrowth GetUserGrowth() => ExecuteLocked(() =>
        {
            using var selectCmd = _connection!.CreateCommand();
            selectCmd.CommandText = "SELECT Id, Level, Xp, TotalXp, ComboDays, BestComboDays, Title, LastXpDate FROM UserProfile WHERE Id = 1";
            using var reader = selectCmd.ExecuteReader();
            if (reader.Read())
            {
                return new UserGrowth
                {
                    Id = reader.GetInt32(0),
                    Level = reader.GetInt32(1),
                    Xp = reader.GetInt32(2),
                    TotalXp = reader.GetInt32(3),
                    ComboDays = reader.GetInt32(4),
                    BestComboDays = reader.GetInt32(5),
                    Title = reader.GetString(6),
                    LastXpDate = reader.IsDBNull(7) ? null : reader.GetString(7)
                };
            }

            // 首次访问：创建默认档案
            using var insertCmd = _connection.CreateCommand();
            insertCmd.CommandText = "INSERT INTO UserProfile (Id, Level, Xp, TotalXp, ComboDays, BestComboDays, Title, LastXpDate) VALUES (1, 1, 0, 0, 0, 0, '初出茅庐', NULL)";
            insertCmd.ExecuteNonQuery();
            return new UserGrowth();
        });

        /// <summary>
        /// 保存用户成长档案（升级系统内部使用，调用方已保证加锁语义）。
        /// </summary>
        public void SaveUserGrowth(UserGrowth growth) => ExecuteLocked(() =>
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = @"
                UPDATE UserProfile SET
                    Level = @level,
                    Xp = @xp,
                    TotalXp = @totalXp,
                    ComboDays = @comboDays,
                    BestComboDays = @bestComboDays,
                    Title = @title,
                    LastXpDate = @lastXpDate
                WHERE Id = 1
            ";
            cmd.Parameters.AddWithValue("@level", growth.Level);
            cmd.Parameters.AddWithValue("@xp", growth.Xp);
            cmd.Parameters.AddWithValue("@totalXp", growth.TotalXp);
            cmd.Parameters.AddWithValue("@comboDays", growth.ComboDays);
            cmd.Parameters.AddWithValue("@bestComboDays", growth.BestComboDays);
            cmd.Parameters.AddWithValue("@title", growth.Title);
            cmd.Parameters.AddWithValue("@lastXpDate", (object?)growth.LastXpDate ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        });

        /// <summary>
        /// 判断指定来源/任务/日期是否已有经验记录（防重复结算）。
        /// </summary>
        public bool HasXpLog(string source, int? taskId, string date) => ExecuteLocked(() =>
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM XpLog WHERE Source = @source AND Date = @date AND (@taskId IS NULL OR TaskId = @taskId)";
            cmd.Parameters.AddWithValue("@source", source);
            cmd.Parameters.AddWithValue("@date", date);
            cmd.Parameters.AddWithValue("@taskId", (object?)taskId ?? DBNull.Value);
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        });

        /// <summary>
        /// 追加一条经验流水。
        /// </summary>
        public void AddXpLog(string source, int amount, int? taskId, string date) => ExecuteLocked(() =>
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO XpLog (Source, Amount, TaskId, Date, CreatedAt, IsDirty)
                VALUES (@source, @amount, @taskId, @date, @createdAt, 1)
            ";
            cmd.Parameters.AddWithValue("@source", source);
            cmd.Parameters.AddWithValue("@amount", amount);
            cmd.Parameters.AddWithValue("@taskId", (object?)taskId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@date", date);
            cmd.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        });

        /// <summary>
        /// 获取今日累计获得经验（用于当日结算/展示）。
        /// </summary>
        public int GetTodayXp() => ExecuteLocked(() =>
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(SUM(Amount), 0) FROM XpLog WHERE Date = @date";
            cmd.Parameters.AddWithValue("@date", DateTime.Today.ToString("yyyy-MM-dd"));
            return Convert.ToInt32(cmd.ExecuteScalar());
        });

        // ==================== 番茄会话数据访问 ====================

        /// <summary>
        /// 获取指定日期已完成/中断的番茄会话数（Completed=1 为完成）。
        /// </summary>
        public (int completed, int interrupted) GetPomodoroCountByDate(string date) => ExecuteLocked(() =>
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(SUM(CASE WHEN Completed = 1 THEN 1 ELSE 0 END), 0), COALESCE(SUM(CASE WHEN Completed = 0 THEN 1 ELSE 0 END), 0) FROM PomodoroSession WHERE Date = @date";
            cmd.Parameters.AddWithValue("@date", date);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
                return (reader.GetInt32(0), reader.GetInt32(1));
            return (0, 0);
        });

        /// <summary>
        /// 获取指定日期专注总分钟数（仅计完成的会话）。
        /// </summary>
        public int GetFocusMinutesByDate(string date) => ExecuteLocked(() =>
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(SUM(DurationMinutes), 0) FROM PomodoroSession WHERE Date = @date AND Completed = 1";
            cmd.Parameters.AddWithValue("@date", date);
            return Convert.ToInt32(cmd.ExecuteScalar());
        });

        /// <summary>
        /// 记录番茄会话。
        /// </summary>
        public void AddPomodoroSession(int? taskId, DateTime startTime, DateTime? endTime, int durationMinutes, bool completed, string date) => ExecuteLocked(() =>
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO PomodoroSession (TaskId, StartTime, EndTime, DurationMinutes, Completed, Date, IsDirty)
                VALUES (@taskId, @startTime, @endTime, @durationMinutes, @completed, @date, 1)
            ";
            cmd.Parameters.AddWithValue("@taskId", (object?)taskId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@startTime", startTime.ToString("O"));
            cmd.Parameters.AddWithValue("@endTime", endTime?.ToString("O") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@durationMinutes", durationMinutes);
            cmd.Parameters.AddWithValue("@completed", completed ? 1 : 0);
            cmd.Parameters.AddWithValue("@date", date);
            cmd.ExecuteNonQuery();
        });

        // ==================== 成就/连击统计 ====================

        /// <summary>
        /// 指定来源的 XP 流水条数（如累计完成任务数 task_complete）。
        /// </summary>
        public int GetXpLogCount(string source) => ExecuteLocked(() =>
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM XpLog WHERE Source = @source";
            cmd.Parameters.AddWithValue("@source", source);
            return Convert.ToInt32(cmd.ExecuteScalar());
        });

        /// <summary>
        /// 累计完成番茄数（Completed=1）。
        /// </summary>
        public int GetCompletedPomodoroTotal() => ExecuteLocked(() =>
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM PomodoroSession WHERE Completed = 1";
            return Convert.ToInt32(cmd.ExecuteScalar());
        });

        /// <summary>
        /// 指定日期每日任务完成数（DailyTaskCompletion 去重任务数）。
        /// </summary>
        public int GetCompletedDailyTaskCountByDate(string date) => ExecuteLocked(() =>
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT COUNT(DISTINCT TaskId) FROM DailyTaskCompletion WHERE Date = @date";
            cmd.Parameters.AddWithValue("@date", date);
            return Convert.ToInt32(cmd.ExecuteScalar());
        });

        /// <summary>
        /// 是否存在指定来源且本地时间满足时段条件的 XP 流水（彩蛋徽章用）。
        /// </summary>
        /// <param name="source">来源</param>
        /// <param name="predicate">对本地时间（DateTime）的判断</param>
        public bool HasXpLogMatchingTime(string source, Func<DateTime, bool> predicate) => ExecuteLocked(() =>
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT CreatedAt FROM XpLog WHERE Source = @source";
            cmd.Parameters.AddWithValue("@source", source);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var raw = reader.GetString(0);
                if (DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var utc))
                {
                    if (predicate(utc.ToLocalTime()))
                        return true;
                }
            }
            return false;
        });

        /// <summary>
        /// 已解锁成就 ID 集合。
        /// </summary>
        public HashSet<string> GetUnlockedAchievements() => ExecuteLocked(() =>
        {
            var ids = new HashSet<string>();
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT AchievementId FROM AchievementUnlocks";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                ids.Add(reader.GetString(0));
            return ids;
        });

        /// <summary>
        /// 记录成就解锁。
        /// </summary>
        public void UnlockAchievement(string achievementId) => ExecuteLocked(() =>
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO AchievementUnlocks (AchievementId, UnlockedAt) VALUES (@id, @at)";
            cmd.Parameters.AddWithValue("@id", achievementId);
            cmd.Parameters.AddWithValue("@at", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        });

        /// <summary>
        /// 获取成就解锁时间（图鉴展示用）。
        /// </summary>
        public DateTime? GetAchievementUnlockedAt(string achievementId) => ExecuteLocked(() =>
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT UnlockedAt FROM AchievementUnlocks WHERE AchievementId = @id";
            cmd.Parameters.AddWithValue("@id", achievementId);
            var raw = cmd.ExecuteScalar()?.ToString();
            if (string.IsNullOrEmpty(raw)) return null;
            return DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var utc)
                ? utc.ToLocalTime()
                : (DateTime?)null;
        });

        // ==================== 每日挑战 ====================

        /// <summary>
        /// 获取指定日期全部每日挑战。
        /// </summary>
        public List<DailyChallenge> GetDailyChallenges(string date) => ExecuteLocked(() =>
        {
            var list = new List<DailyChallenge>();
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT Date, ChallengeId, Progress, Target, Completed FROM DailyChallenge WHERE Date = @date ORDER BY ChallengeId";
            cmd.Parameters.AddWithValue("@date", date);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new DailyChallenge
                {
                    Date = reader.GetString(0),
                    Type = reader.GetString(1),
                    Progress = reader.GetInt32(2),
                    Target = reader.GetInt32(3),
                    Completed = reader.GetInt32(4) == 1
                });
            }
            return list;
        });

        /// <summary>
        /// 保存指定日期全部每日挑战（逐条插入或替换）。
        /// </summary>
        public void SaveDailyChallenges(string date, List<DailyChallenge> challenges) => ExecuteLocked(() =>
        {
            using var transaction = _connection!.BeginTransaction();
            try
            {
                using (var deleteCmd = _connection.CreateCommand())
                {
                    deleteCmd.Transaction = transaction;
                    deleteCmd.CommandText = "DELETE FROM DailyChallenge WHERE Date = @date";
                    deleteCmd.Parameters.AddWithValue("@date", date);
                    deleteCmd.ExecuteNonQuery();
                }

                foreach (var c in challenges)
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT INTO DailyChallenge (Date, ChallengeId, Progress, Target, Completed)
                        VALUES (@date, @id, @progress, @target, @completed)
                    ";
                    cmd.Parameters.AddWithValue("@date", c.Date);
                    cmd.Parameters.AddWithValue("@id", c.Type);
                    cmd.Parameters.AddWithValue("@progress", c.Progress);
                    cmd.Parameters.AddWithValue("@target", c.Target);
                    cmd.Parameters.AddWithValue("@completed", c.Completed ? 1 : 0);
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); } catch { /* 已回滚或连接异常 */ }
                throw;
            }
        });

        // ==================== 成长数据同步 ====================

        /// <summary>
        /// 确保升级系统表具备同步所需列（XpLog.IsDirty / PomodoroSession.IsDirty）。
        /// </summary>
        public void EnsureGrowthSyncColumns()
        {
            ExecuteLocked(() =>
            {
                var columns = new (string table, string column, string def)[]
                {
                    ("XpLog", "IsDirty", "INTEGER DEFAULT 0"),
                    ("PomodoroSession", "IsDirty", "INTEGER DEFAULT 0")
                };
                foreach (var (table, column, def) in columns)
                {
                    try
                    {
                        using var checkCmd = _connection!.CreateCommand();
                        checkCmd.CommandText = $"SELECT {column} FROM {table} LIMIT 1";
                        checkCmd.ExecuteScalar();
                    }
                    catch
                    {
                        try
                        {
                            using var alterCmd = _connection!.CreateCommand();
                            alterCmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {def}";
                            alterCmd.ExecuteNonQuery();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"ALTER {table} ADD {column} failed: {ex.Message}");
                        }
                    }
                }
            });
        }

        /// <summary>
        /// 待同步的 XP 流水（IsDirty=1）。
        /// </summary>
        public List<XpLogEntry> GetDirtyXpLogs() => ExecuteLocked(() =>
        {
            var list = new List<XpLogEntry>();
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT Id, Source, Amount, TaskId, Date, CreatedAt FROM XpLog WHERE IsDirty = 1";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new XpLogEntry
                {
                    Id = reader.GetInt32(0),
                    Source = reader.GetString(1),
                    Amount = reader.GetInt32(2),
                    TaskId = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3),
                    Date = reader.GetString(4),
                    CreatedAt = ReadDateTime(reader, "CreatedAt") ?? DateTime.UtcNow
                });
            }
            return list;
        });

        /// <summary>
        /// 标记 XP 流水已同步。
        /// </summary>
        public void MarkXpLogSynced(int id) => ExecuteLocked(() =>
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "UPDATE XpLog SET IsDirty = 0 WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        });

        /// <summary>
        /// 待同步的番茄会话（IsDirty=1）。
        /// </summary>
        public List<PomodoroSessionEntry> GetDirtyPomodoroSessions() => ExecuteLocked(() =>
        {
            var list = new List<PomodoroSessionEntry>();
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT Id, TaskId, StartTime, EndTime, DurationMinutes, Completed, Date FROM PomodoroSession WHERE IsDirty = 1";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new PomodoroSessionEntry
                {
                    Id = reader.GetInt32(0),
                    TaskId = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1),
                    StartTime = ReadDateTime(reader, "StartTime") ?? DateTime.UtcNow,
                    EndTime = ReadDateTime(reader, "EndTime"),
                    DurationMinutes = reader.GetInt32(4),
                    Completed = reader.GetInt32(5) == 1,
                    Date = reader.GetString(6)
                });
            }
            return list;
        });

        /// <summary>
        /// 标记番茄会话已同步。
        /// </summary>
        public void MarkPomodoroSynced(int id) => ExecuteLocked(() =>
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "UPDATE PomodoroSession SET IsDirty = 0 WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        });

        /// <summary>
        /// 近 N 天每日获得经验（等级曲线图数据）。
        /// </summary>
        public List<(DateTime date, int xp)> GetDailyXpLastDays(int days) => ExecuteLocked(() =>
        {
            var result = new List<(DateTime date, int xp)>();
            var start = DateTime.Today.AddDays(-(days - 1)).ToString("yyyy-MM-dd");
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT Date, SUM(Amount) FROM XpLog WHERE Date >= @start GROUP BY Date";
            cmd.Parameters.AddWithValue("@start", start);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (DateTime.TryParseExact(reader.GetString(0), "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var d))
                    result.Add((d, reader.GetInt32(1)));
            }
            // 补全缺失日期（0 XP）
            for (int i = days - 1; i >= 0; i--)
            {
                var date = DateTime.Today.AddDays(-i);
                if (!result.Any(r => r.date == date))
                    result.Add((date, 0));
            }
            return result.OrderBy(r => r.date).ToList();
        });

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
