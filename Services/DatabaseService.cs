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
        private static volatile DatabaseService? _instance; // L2 修复：volatile 保证双检锁发布安全
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
                            // S2 修复：先在局部变量上完成初始化，成功后才发布到 _instance，
                            // 避免初始化抛异常后留下 _connection 为 null 的僵尸单例
                            var instance = new DatabaseService();
                            instance.Initialize();
                            _instance = instance;
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
            catch (SqliteException ex) when (IsCorruptionError(ex))
            {
                // 仅在数据库文件确认损坏时才备份后重建（S1 修复：
                // 瞬时占用/磁盘满/杀软锁定等可恢复错误不再触发毁坏性删库）
                System.Diagnostics.Debug.WriteLine($"数据库文件损坏: {ex.Message}，将备份后重建");

                try
                {
                    _connection?.Dispose();
                    _connection = null;
                    BackupCorruptedDatabase();
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

            // M1 修复：为存活任务的 SyncId 建唯一部分索引，从根上杜绝重复 SyncId 行
            // （历史数据若已有重复会创建失败，记录但不阻断启动）
            try
            {
                using var idxCmd = _connection.CreateCommand();
                idxCmd.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS idx_tasks_syncid_live ON Tasks(SyncId) WHERE SyncId IS NOT NULL AND IsDeleted = 0";
                idxCmd.ExecuteNonQuery();
            }
            catch (Exception idxEx)
            {
                System.Diagnostics.Debug.WriteLine($"创建 SyncId 唯一索引失败(可能存在历史重复数据): {idxEx.Message}");
            }

            // ===== 升级系统表 =====
            CreateGrowthTables();
            EnsureGrowthSyncColumns();
        }

        /// <summary>
        /// 判断是否为数据库文件损坏类错误（S1 修复）。
        /// 只有确认损坏（SQLITE_CORRUPT / SQLITE_NOTADB）才允许走"备份后重建"路径，
        /// 其余异常（占用、权限、磁盘满等）直接向上抛出，避免误删用户数据。
        /// </summary>
        private static bool IsCorruptionError(SqliteException ex)
        {
            // 11 = SQLITE_CORRUPT, 26 = SQLITE_NOTADB
            if (ex.SqliteErrorCode == 11 || ex.SqliteErrorCode == 26)
                return true;
            // 扩展错误码兜底（如 SQLITE_CORRUPT_VFS 等变体）
            var ext = ex.SqliteExtendedErrorCode & 0xFF;
            if (ext == 11 || ext == 26)
                return true;
            var msg = ex.Message ?? string.Empty;
            return msg.Contains("corrupt", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("not a database", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("malformed", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 备份损坏的数据库文件（含 -wal/-shm），带时间戳保留最近 3 份，然后删除原文件重建。
        /// </summary>
        private void BackupCorruptedDatabase()
        {
            if (!File.Exists(_dbPath)) return;

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var candidates = new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" };
            try
            {
                foreach (var src in candidates)
                {
                    if (!File.Exists(src)) continue;
                    File.Copy(src, $"{src}.corrupted-{stamp}.bak", overwrite: false);
                }
                System.Diagnostics.Debug.WriteLine($"已备份损坏的数据库到: {_dbPath}.corrupted-{stamp}.bak");
            }
            catch (Exception backupEx)
            {
                // 备份失败也要继续尝试重建（否则应用完全不可用），但必须记录
                System.Diagnostics.Debug.WriteLine($"备份损坏数据库失败: {backupEx.Message}");
            }

            foreach (var src in candidates)
            {
                try { if (File.Exists(src)) File.Delete(src); }
                catch (Exception delEx)
                {
                    System.Diagnostics.Debug.WriteLine($"删除损坏数据库文件失败: {src}: {delEx.Message}");
                }
            }

            // 只保留最近 3 份损坏备份，避免无限膨胀
            try
            {
                var dir = Path.GetDirectoryName(_dbPath) ?? ".";
                var name = Path.GetFileName(_dbPath);
                var oldBackups = Directory.GetFiles(dir, $"{name}.corrupted-*.bak")
                    .Concat(Directory.GetFiles(dir, $"{name}-*.corrupted-*.bak"))
                    .OrderByDescending(f => f)
                    .Skip(3);
                foreach (var old in oldBackups)
                {
                    try { File.Delete(old); } catch { /* 尽力清理 */ }
                }
            }
            catch { /* 清理失败不影响主流程 */ }
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
                    LastXpDate TEXT,
                    LastComboSettledDate TEXT
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
                { "IsDeleted", "INTEGER DEFAULT 0" },
                // S7 修复：本地编辑时间戳（UTC），用于同步冲突时与云端 UpdatedAt 做真正的 LWW 比较
                { "LocalUpdatedAt", "TEXT" },
                // v5.3 回收站：软删除时间戳（本地时间 O 格式），30 天自动清除依据
                { "DeletedAt", "TEXT" }
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
                        // M3 修复：迁移失败不再静默吞掉——缺失列会让后续写入持续报错，
                        // 提前失败并给出明确原因远好于"完成任务不发经验"这类难排查故障
                        throw new InvalidOperationException($"数据库迁移失败：ALTER TABLE Tasks ADD COLUMN {column.Key}", alterEx);
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
                INSERT INTO Tasks (Title, Type, Priority, IsCompleted, CreatedAt, Deadline, Description, Tags, SortOrder, EstimatedMinutes, ActualMinutes, SubTasksJson, IsDirty, LocalUpdatedAt)
                VALUES (@title, @type, @priority, @completed, @createdAt, @deadline, @description, @tags, @sortOrder, @estimatedMinutes, @actualMinutes, @subTasksJson, 1, @localUpdatedAt);
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
            cmd.Parameters.AddWithValue("@localUpdatedAt", DateTime.UtcNow.ToString("O"));
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
                INSERT INTO Tasks (Title, Type, Priority, IsCompleted, CreatedAt, Deadline, CompletedAt, Description, Tags, SortOrder, EstimatedMinutes, ActualMinutes, SubTasksJson, SyncId, IsDirty, LastSyncedAt, IsDeleted, LocalUpdatedAt)
                VALUES (@title, @type, @priority, @completed, @createdAt, @deadline, @completedAt, @description, @tags, @sortOrder, @estimatedMinutes, @actualMinutes, @subTasksJson, @syncId, @isDirty, @lastSyncedAt, @isDeleted, @localUpdatedAt);
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
            cmd.Parameters.AddWithValue("@localUpdatedAt", task.LocalUpdatedAt?.ToString("O") ?? DateTime.UtcNow.ToString("O"));
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
                    IsDirty = 1,
                    LocalUpdatedAt = @localUpdatedAt
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
            cmd.Parameters.AddWithValue("@localUpdatedAt", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        });

        public void DeleteTask(int id) => ExecuteLocked(() =>
        {
            // 软删除：标记 IsDeleted + IsDirty + DeletedAt（v5.3 回收站 30 天保留期起点），同步时会上传到云端
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "UPDATE Tasks SET IsDeleted = 1, IsDirty = 1, DeletedAt = @deletedAt, LocalUpdatedAt = @localUpdatedAt WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@deletedAt", DateTime.Now.ToString("O"));
            cmd.Parameters.AddWithValue("@localUpdatedAt", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        });

        /// <summary>
        /// v5.3 回收站：从回收站恢复任务（清除软删标记，标记脏待同步）。
        /// </summary>
        public void RestoreTask(int id) => ExecuteLocked(() =>
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "UPDATE Tasks SET IsDeleted = 0, DeletedAt = NULL, IsDirty = 1, LocalUpdatedAt = @localUpdatedAt WHERE Id = @id AND IsDeleted = 1";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@localUpdatedAt", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        });

        /// <summary>
        /// v5.3 回收站：查询全部软删除任务（按删除时间倒序）。
        /// </summary>
        public List<TaskItem> GetDeletedTasks() => ExecuteLocked(() =>
        {
            var tasks = new List<TaskItem>();
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT * FROM Tasks WHERE IsDeleted = 1 ORDER BY DeletedAt DESC";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) tasks.Add(ReadTask(reader));
            return tasks;
        });

        /// <summary>回收站保留期（天）。</summary>
        internal const int TrashRetentionDays = 30;

        /// <summary>
        /// v5.3 回收站：彻底删除单个任务（硬删，连带清理子表记录）。
        /// 返回是否删除了行。
        /// </summary>
        public bool PurgeTask(int id) => ExecuteLocked(() =>
        {
            using var transaction = _connection!.BeginTransaction();
            try
            {
                int affected;
                using (var cmd = _connection!.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = "DELETE FROM Tasks WHERE Id = @id AND IsDeleted = 1";
                    cmd.Parameters.AddWithValue("@id", id);
                    affected = cmd.ExecuteNonQuery();
                }
                CleanupOrphanRecords(transaction);
                transaction.Commit();
                return affected > 0;
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }
        });

        /// <summary>
        /// v5.3 回收站：清除超过保留期的软删除任务，并清理指向失效任务的子表孤儿记录。
        /// 启动时调用。返回清除的任务数。
        /// </summary>
        public int PurgeExpiredDeletedTasks() => ExecuteLocked(() =>
        {
            var cutoff = DateTime.Now.AddDays(-TrashRetentionDays).ToString("O");
            using var transaction = _connection!.BeginTransaction();
            try
            {
                int purged;
                using (var cmd = _connection!.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    // IsDirty=0 守卫：未上云的删除墓碑不能本地清除，否则下次同步会"复活"
                    cmd.CommandText = "DELETE FROM Tasks WHERE IsDeleted = 1 AND IsDirty = 0 AND DeletedAt IS NOT NULL AND DeletedAt < @cutoff";
                    cmd.Parameters.AddWithValue("@cutoff", cutoff);
                    purged = cmd.ExecuteNonQuery();
                }
                if (purged > 0)
                    CleanupOrphanRecords(transaction);
                transaction.Commit();
                return purged;
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }
        });

        /// <summary>v5.3：清理子表中指向已不存在任务的孤儿记录（与导入恢复同口径）。</summary>
        private void CleanupOrphanRecords(SqliteTransaction transaction)
        {
            foreach (var table in new[] { "DailyTaskCompletion", "XpLog", "PomodoroSession" })
            {
                using var cmd = _connection!.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = $"DELETE FROM {table} WHERE TaskId IS NOT NULL AND TaskId NOT IN (SELECT Id FROM Tasks)";
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 事务化导入任务（M4 修复）：单事务包裹整批插入（失败整体回滚），
        /// 且按 SyncId 去重——已存在同 SyncId 存活任务时跳过，防止重复导入产生整套重复数据。
        /// 返回实际导入条数。
        /// </summary>
        public int ImportTasksUnique(List<TaskItem> tasks) => ExecuteLocked(() =>
        {
            using var transaction = _connection!.BeginTransaction();
            try
            {
                int imported = 0;
                foreach (var task in tasks)
                {
                    // SyncId 去重：已存在同 SyncId 的存活任务则跳过
                    if (!string.IsNullOrEmpty(task.SyncId) && GetTaskBySyncIdCore(task.SyncId) != null)
                        continue;

                    using var cmd = _connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT INTO Tasks (Title, Type, Priority, IsCompleted, CreatedAt, Deadline, CompletedAt, Description, Tags, SortOrder, EstimatedMinutes, ActualMinutes, SubTasksJson, SyncId, IsDirty, LastSyncedAt, IsDeleted, LocalUpdatedAt)
                        VALUES (@title, @type, @priority, @completed, @createdAt, @deadline, @completedAt, @description, @tags, @sortOrder, @estimatedMinutes, @actualMinutes, @subTasksJson, @syncId, @isDirty, @lastSyncedAt, @isDeleted, @localUpdatedAt)
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
                    cmd.Parameters.AddWithValue("@localUpdatedAt", DateTime.UtcNow.ToString("O"));
                    cmd.ExecuteNonQuery();
                    imported++;
                }
                transaction.Commit();
                return imported;
            }
            catch
            {
                try { transaction.Rollback(); } catch { /* 已回滚或连接异常 */ }
                throw;
            }
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
            // L3 修复：在 C# 侧解析 LastSyncedAt 后比较时间，
            // 避免历史数据混入本地偏移格式时 SQL 字符串比较失效导致漏删
            var cutoff = DateTime.UtcNow.AddDays(-daysOld);
            var ids = new List<int>();
            using (var selectCmd = _connection!.CreateCommand())
            {
                selectCmd.CommandText = "SELECT Id, LastSyncedAt FROM Tasks WHERE IsDeleted = 1 AND IsDirty = 0 AND LastSyncedAt IS NOT NULL";
                using var reader = selectCmd.ExecuteReader();
                while (reader.Read())
                {
                    var synced = ReadDateTime(reader, "LastSyncedAt");
                    if (synced.HasValue && synced.Value < cutoff)
                        ids.Add(reader.GetInt32(0));
                }
            }

            foreach (var id in ids)
            {
                using var delCmd = _connection.CreateCommand();
                delCmd.CommandText = "DELETE FROM Tasks WHERE Id = @id";
                delCmd.Parameters.AddWithValue("@id", id);
                delCmd.ExecuteNonQuery();
            }
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
                IsDeleted = reader.IsDBNull(reader.GetOrdinal("IsDeleted")) ? false : reader.GetInt32(reader.GetOrdinal("IsDeleted")) == 1,
                LocalUpdatedAt = ReadDateTime(reader, "LocalUpdatedAt"),
                DeletedAt = ReadDateTime(reader, "DeletedAt")
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
            // L1 修复：解析失败不再无声回退，留痕便于排查"时间莫名变成现在"类问题
            System.Diagnostics.Debug.WriteLine($"[DatabaseService] 列 {columnName} 日期解析失败，原始值: {raw}");
            return null;
        }

        // ==================== 设置 ====================

        public string? GetSetting(string key) => ExecuteLocked(() => GetSettingCore(key));

        /// <summary>无锁版设置读取（供已持有 _dbLock 的内部方法复用）</summary>
        private string? GetSettingCore(string key)
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT Value FROM Settings WHERE Key = @key";
            cmd.Parameters.AddWithValue("@key", key);
            var result = cmd.ExecuteScalar();
            return result?.ToString();
        }

        public void SetSetting(string key, string value) => ExecuteLocked(() => SetSettingCore(key, value));

        /// <summary>无锁版设置写入（供已持有 _dbLock 的内部方法复用，M38d 修复重入死锁）</summary>
        private void SetSettingCore(string key, string value)
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO Settings (Key, Value) VALUES (@key, @value)
            ";
            cmd.Parameters.AddWithValue("@key", key);
            cmd.Parameters.AddWithValue("@value", value);
            cmd.ExecuteNonQuery();
        }

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
            // M20 修复：过滤已删任务的孤儿记录
            cmd.CommandText = @"
                SELECT d.TaskId FROM DailyTaskCompletion d
                INNER JOIN Tasks t ON t.Id = d.TaskId AND t.IsDeleted = 0
                WHERE d.Date = @date
            ";
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
            cmd.CommandText = "UPDATE Tasks SET IsDirty = 1, LocalUpdatedAt = @localUpdatedAt WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", taskId);
            cmd.Parameters.AddWithValue("@localUpdatedAt", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        });

        /// <summary>
        /// v5.3 热力图：统计区间内每日完成次数（每日任务完成记录 + 当日完成的截止任务）。
        /// 返回 yyyy-MM-dd → 次数。
        /// </summary>
        public Dictionary<string, int> GetHeatmapCounts(DateTime start, DateTime end) => ExecuteLocked(() =>
        {
            var result = new Dictionary<string, int>();
            var startStr = start.ToString("yyyy-MM-dd");
            var endStr = end.ToString("yyyy-MM-dd");

            // 每日任务完成记录
            using (var cmd = _connection!.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT d.Date, COUNT(*) FROM DailyTaskCompletion d
                    INNER JOIN Tasks t ON t.Id = d.TaskId AND t.IsDeleted = 0
                    WHERE d.Date >= @start AND d.Date <= @end
                    GROUP BY d.Date";
                cmd.Parameters.AddWithValue("@start", startStr);
                cmd.Parameters.AddWithValue("@end", endStr);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    result[reader.GetString(0)] = Convert.ToInt32(reader.GetInt64(1));
            }

            // 截止任务按 CompletedAt 当日计
            using (var cmd = _connection!.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT substr(CompletedAt, 1, 10), COUNT(*) FROM Tasks
                    WHERE Type = @type AND IsCompleted = 1 AND IsDeleted = 0
                      AND CompletedAt IS NOT NULL
                      AND substr(CompletedAt, 1, 10) >= @start AND substr(CompletedAt, 1, 10) <= @end
                    GROUP BY substr(CompletedAt, 1, 10)";
                cmd.Parameters.AddWithValue("@type", (int)TaskType.Deadline);
                cmd.Parameters.AddWithValue("@start", startStr);
                cmd.Parameters.AddWithValue("@end", endStr);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var day = reader.GetString(0);
                    result[day] = result.TryGetValue(day, out var cur) ? cur + Convert.ToInt32(reader.GetInt64(1)) : Convert.ToInt32(reader.GetInt64(1));
                }
            }
            return result;
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
            // M20 修复：JOIN 过滤已删任务的孤儿完成记录
            cmd.CommandText = @"
                SELECT d.Date, d.TaskId FROM DailyTaskCompletion d
                INNER JOIN Tasks t ON t.Id = d.TaskId AND t.IsDeleted = 0
                WHERE d.Date >= @startDate
            ";
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

        /// <summary>
        /// 指定日期时点已存在的每日任务数（M19 修复）。
        /// 用 CreatedAt 前缀比较实现"当时有多少任务"的近似快照，
        /// 供连击结算按被判定日期取基准，避免今天增删任务追溯改写历史连击。
        /// </summary>
        public int GetDailyTaskCountAsOf(string date) => ExecuteLocked(() =>
        {
            using var cmd = _connection!.CreateCommand();
            // CreatedAt 为 "yyyy-MM-ddTHH:mm..." 格式文本，前 10 位即日期
            cmd.CommandText = "SELECT COUNT(*) FROM Tasks WHERE Type = @type AND IsDeleted = 0 AND substr(CreatedAt, 1, 10) <= @date";
            cmd.Parameters.AddWithValue("@type", (int)TaskType.Daily);
            cmd.Parameters.AddWithValue("@date", date);
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
                    // S11 修复：排序变更置脏，使 SortOrder 能同步到云端
                    cmd.CommandText = "UPDATE Tasks SET SortOrder = @order, IsDirty = 1, LocalUpdatedAt = @localUpdatedAt WHERE Id = @id";
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@order", order);
                    cmd.Parameters.AddWithValue("@localUpdatedAt", DateTime.UtcNow.ToString("O"));
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
        /// 标记任务已同步。
        /// S7 修复：带乐观守卫——仅当任务的 LocalUpdatedAt 仍等于上传时的快照值时才清除 IsDirty，
        /// 防止"上传期间用户又编辑了任务"导致新修改被误标为已同步而丢失。
        /// </summary>
        public void MarkTaskSynced(int localId, string syncId, string? expectedLocalUpdatedAt = null)
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
                      AND (@expected IS NULL OR LocalUpdatedAt IS NULL OR LocalUpdatedAt = @expected)
                ";
                cmd.Parameters.AddWithValue("@id", localId);
                cmd.Parameters.AddWithValue("@syncId", syncId);
                cmd.Parameters.AddWithValue("@syncedAt", DateTime.UtcNow.ToString("O"));
                cmd.Parameters.AddWithValue("@expected", expectedLocalUpdatedAt ?? (object)DBNull.Value);
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
                            LastSyncedAt = @syncedAt,
                            LocalUpdatedAt = @localUpdatedAt
                        WHERE SyncId = @syncId
                    ";
                    cmd.Parameters.AddWithValue("@syncId", task.SyncId);
                    cmd.Parameters.AddWithValue("@title", task.Title);
                    cmd.Parameters.AddWithValue("@type", (int)task.Type);
                    cmd.Parameters.AddWithValue("@priority", (int)task.Priority);
                    cmd.Parameters.AddWithValue("@completed", task.IsCompleted ? 1 : 0);
                    AddRemoteTimeParams(cmd, task);
                    cmd.Parameters.AddWithValue("@description", task.Description ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@tags", task.Tags ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@sortOrder", task.SortOrder);
                    cmd.Parameters.AddWithValue("@subTasksJson", task.SubTasksJson ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@isDeleted", task.IsDeleted ? 1 : 0);
                    cmd.Parameters.AddWithValue("@syncedAt", DateTime.UtcNow.ToString("O"));
                    cmd.Parameters.AddWithValue("@localUpdatedAt", DateTime.UtcNow.ToString("O"));
                    cmd.ExecuteNonQuery();
                }
                else
                {
                    // 插入新任务
                    using var cmd = _connection!.CreateCommand();
                    cmd.CommandText = @"
                        INSERT INTO Tasks (Title, Type, Priority, IsCompleted, CreatedAt, Deadline, CompletedAt, Description, Tags, SortOrder, SubTasksJson, SyncId, IsDirty, LastSyncedAt, IsDeleted, LocalUpdatedAt)
                        VALUES (@title, @type, @priority, @completed, @createdAt, @deadline, @completedAt, @description, @tags, @sortOrder, @subTasksJson, @syncId, 0, @syncedAt, @isDeleted, @localUpdatedAt)
                    ";
                    cmd.Parameters.AddWithValue("@title", task.Title);
                    cmd.Parameters.AddWithValue("@type", (int)task.Type);
                    cmd.Parameters.AddWithValue("@priority", (int)task.Priority);
                    cmd.Parameters.AddWithValue("@completed", task.IsCompleted ? 1 : 0);
                    AddRemoteTimeParams(cmd, task);
                    cmd.Parameters.AddWithValue("@description", task.Description ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@tags", task.Tags ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@sortOrder", task.SortOrder);
                    cmd.Parameters.AddWithValue("@subTasksJson", task.SubTasksJson ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@syncId", task.SyncId);
                    cmd.Parameters.AddWithValue("@isDeleted", task.IsDeleted ? 1 : 0);
                    cmd.Parameters.AddWithValue("@syncedAt", DateTime.UtcNow.ToString("O"));
                    cmd.Parameters.AddWithValue("@localUpdatedAt", DateTime.UtcNow.ToString("O"));
                    cmd.ExecuteNonQuery();
                }
            });
        }

        /// <summary>
        /// 远端任务时间列参数（M2 修复）：远端时间为 UTC，统一转本地偏移格式落库，
        /// 避免同一列混存 "...Z" 与 "+08:00" 两种文本导致 ORDER BY / 范围比较失真。
        /// </summary>
        private static void AddRemoteTimeParams(SqliteCommand cmd, TaskItem task)
        {
            cmd.Parameters.AddWithValue("@createdAt", task.CreatedAt.ToLocalTime().ToString("O"));
            cmd.Parameters.AddWithValue("@deadline", task.Deadline?.ToLocalTime().ToString("O") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@completedAt", task.CompletedAt?.ToLocalTime().ToString("O") ?? (object)DBNull.Value);
        }

        /// <summary>
        /// 原子替换全部任务（备份恢复用）：单事务内物理删除现有任务 + 按原始 Id 插入恢复的任务
        /// + 清理子表孤儿记录。任一步失败整体回滚，避免"先删光再导入失败"导致数据丢失。
        /// </summary>
        public void ReplaceAllTasks(List<TaskItem> tasks) => ExecuteLocked(() =>
        {
            using var transaction = _connection!.BeginTransaction();
            try
            {
                // 1. 物理删除全部现有任务（S3 修复：原实现软删后按新自增 Id 插入，
                //    导致 DailyTaskCompletion/XpLog/PomodoroSession 的 TaskId 全部孤儿化，
                //    且新旧行并存产生重复 SyncId 干扰同步匹配。
                //    恢复语义 = 回到备份快照，物理删除 + 保留备份中的原始 Id，
                //    使子表引用在"同源恢复"场景下天然保持有效）
                using (var deleteCmd = _connection.CreateCommand())
                {
                    deleteCmd.Transaction = transaction;
                    deleteCmd.CommandText = "DELETE FROM Tasks";
                    deleteCmd.ExecuteNonQuery();
                }

                // 2. 插入恢复的任务（保留原始 Id 与 SyncId/IsDirty，避免云端重复）
                foreach (var task in tasks)
                {
                    using var cmd = _connection!.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT INTO Tasks (Id, Title, Type, Priority, IsCompleted, CreatedAt, Deadline, CompletedAt, Description, Tags, SortOrder, EstimatedMinutes, ActualMinutes, SubTasksJson, SyncId, IsDirty, LastSyncedAt, IsDeleted, LocalUpdatedAt)
                        VALUES (@id, @title, @type, @priority, @completed, @createdAt, @deadline, @completedAt, @description, @tags, @sortOrder, @estimatedMinutes, @actualMinutes, @subTasksJson, @syncId, @isDirty, @lastSyncedAt, @isDeleted, @localUpdatedAt)
                    ";
                    cmd.Parameters.AddWithValue("@id", task.Id);
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
                    cmd.Parameters.AddWithValue("@localUpdatedAt", task.LocalUpdatedAt?.ToString("O") ?? DateTime.UtcNow.ToString("O"));
                    cmd.ExecuteNonQuery();
                }

                // 3. 清理子表中指向已不存在任务的孤儿记录（跨源恢复时旧 Id 可能失效），
                //    避免污染连击结算与统计
                foreach (var table in new[] { "DailyTaskCompletion", "XpLog", "PomodoroSession" })
                {
                    using var cleanupCmd = _connection.CreateCommand();
                    cleanupCmd.Transaction = transaction;
                    cleanupCmd.CommandText = $"DELETE FROM {table} WHERE TaskId IS NOT NULL AND TaskId NOT IN (SELECT Id FROM Tasks)";
                    cleanupCmd.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); } catch { /* 已回滚或连接异常 */ }
                throw;
            }
        });

        // ==================== 多用户隔离（S6 修复） ====================

        /// <summary>
        /// 确保本地数据归属当前登录用户（S6 修复）。
        /// 检测到切换账号时清空本地业务数据，防止前一用户的任务/成长数据
        /// 被显示给新用户、或在下次同步时被上传到新账号名下。
        /// 同一账号重复登录不受影响（保留本地数据）。
        /// </summary>
        public void EnsureUserScope(string userId)
        {
            if (string.IsNullOrEmpty(userId)) return;

            ExecuteLocked(() =>
            {
                var lastUserId = GetSettingCore("LastUserId");
                if (lastUserId == userId)
                    return; // 同一用户，保留本地数据

                if (lastUserId == null)
                {
                    // 首次运行新版本（无归属记录）：只登记归属、不清库。
                    // 运行时验证发现"未知即清库"会在老用户升级首启时误清未同步的离线数据；
                    // 云端有完整数据时可恢复，但离线数据将丢失。保守处理更安全。
                    // M38d 修复：此处已持有 _dbLock，必须用无锁版 SetSettingCore，
                    // 否则 SemaphoreSlim 不可重入 => 自己等自己 => 全库死锁（登录卡"登录中"）
                    SetSettingCore("LastUserId", userId);
                    return;
                }

                using var transaction = _connection!.BeginTransaction();
                try
                {
                    foreach (var table in new[] { "Tasks", "DailyTaskCompletion", "XpLog", "PomodoroSession", "AchievementUnlocks", "DailyChallenge" })
                    {
                        using var cmd = _connection.CreateCommand();
                        cmd.Transaction = transaction;
                        cmd.CommandText = $"DELETE FROM {table}";
                        cmd.ExecuteNonQuery();
                    }

                    // 重置成长档案
                    using (var resetProfile = _connection.CreateCommand())
                    {
                        resetProfile.Transaction = transaction;
                        resetProfile.CommandText = "DELETE FROM UserProfile";
                        resetProfile.ExecuteNonQuery();
                    }

                    // 清理同步游标（旧账号的增量游标对新账号无意义）
                    using (var clearCursor = _connection.CreateCommand())
                    {
                        clearCursor.Transaction = transaction;
                        clearCursor.CommandText = "DELETE FROM Settings WHERE Key LIKE 'LastSyncTimeUtc%'";
                        clearCursor.ExecuteNonQuery();
                    }

                    // 记录当前用户
                    using (var saveUser = _connection.CreateCommand())
                    {
                        saveUser.Transaction = transaction;
                        saveUser.CommandText = @"
                            INSERT INTO Settings (Key, Value) VALUES ('LastUserId', @uid)
                            ON CONFLICT(Key) DO UPDATE SET Value = @uid
                        ";
                        saveUser.Parameters.AddWithValue("@uid", userId);
                        saveUser.ExecuteNonQuery();
                    }

                    transaction.Commit();
                    System.Diagnostics.Debug.WriteLine($"检测到账号切换，已清空本地数据（{lastUserId ?? "无"} → {userId}）");
                }
                catch
                {
                    try { transaction.Rollback(); } catch { /* 已回滚或连接异常 */ }
                    throw;
                }
            });
        }

        // ==================== 升级系统数据访问 ====================

        /// <summary>
        /// 获取用户成长档案；不存在则创建默认行（Lv.1）。
        /// </summary>
        public UserGrowth GetUserGrowth() => ExecuteLocked(GetUserGrowthCore);

        private UserGrowth GetUserGrowthCore()
        {
            using var selectCmd = _connection!.CreateCommand();
            selectCmd.CommandText = "SELECT Id, Level, Xp, TotalXp, ComboDays, BestComboDays, Title, LastXpDate, LastComboSettledDate FROM UserProfile WHERE Id = 1";
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
                    LastXpDate = reader.IsDBNull(7) ? null : reader.GetString(7),
                    LastComboSettledDate = reader.IsDBNull(8) ? null : reader.GetString(8)
                };
            }

            // 首次访问：创建默认档案
            using var insertCmd = _connection.CreateCommand();
            insertCmd.CommandText = "INSERT INTO UserProfile (Id, Level, Xp, TotalXp, ComboDays, BestComboDays, Title, LastXpDate) VALUES (1, 1, 0, 0, 0, 0, '初出茅庐', NULL)";
            insertCmd.ExecuteNonQuery();
            return new UserGrowth();
        }

        /// <summary>
        /// 保存用户成长档案（升级系统内部使用，调用方已保证加锁语义）。
        /// </summary>
        public void SaveUserGrowth(UserGrowth growth) => ExecuteLocked(() => SaveUserGrowthCore(growth));

        private void SaveUserGrowthCore(UserGrowth growth)
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
                    LastXpDate = @lastXpDate,
                    LastComboSettledDate = @lastComboSettledDate
                WHERE Id = 1
            ";
            cmd.Parameters.AddWithValue("@level", growth.Level);
            cmd.Parameters.AddWithValue("@xp", growth.Xp);
            cmd.Parameters.AddWithValue("@totalXp", growth.TotalXp);
            cmd.Parameters.AddWithValue("@comboDays", growth.ComboDays);
            cmd.Parameters.AddWithValue("@bestComboDays", growth.BestComboDays);
            cmd.Parameters.AddWithValue("@title", growth.Title);
            cmd.Parameters.AddWithValue("@lastXpDate", (object?)growth.LastXpDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@lastComboSettledDate", (object?)growth.LastComboSettledDate ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// 判断指定来源/任务/日期是否已有经验记录（防重复结算）。
        /// </summary>
        public bool HasXpLog(string source, int? taskId, string date) => ExecuteLocked(() => HasXpLogCore(source, taskId, date));

        private bool HasXpLogCore(string source, int? taskId, string date)
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM XpLog WHERE Source = @source AND Date = @date AND (@taskId IS NULL OR TaskId = @taskId)";
            cmd.Parameters.AddWithValue("@source", source);
            cmd.Parameters.AddWithValue("@date", date);
            cmd.Parameters.AddWithValue("@taskId", (object?)taskId ?? DBNull.Value);
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }

        /// <summary>
        /// M14 修复：原子发放经验——防重检查、档案更新、流水写入三步在单次加锁 + 单事务内完成，
        /// 消除并发绕过（两线程同时通过查重）与崩溃窗口（档案已写、流水未写导致重复发奖）。
        /// mutate 在事务内对档案做业务变更（加 XP/升级/连击等）并返回实发经验值；
        /// 返回值 ≤ 0 视为未发放（整体回滚）。方法返回 false 表示命中防重未发放。
        /// </summary>
        public bool TryRewardXp(string source, int? taskId, string date, bool dedup, Func<UserGrowth, int> mutate)
            => ExecuteLocked(() =>
            {
                if (dedup && HasXpLogCore(source, taskId, date))
                    return false;

                using var transaction = _connection!.BeginTransaction();
                try
                {
                    var growth = GetUserGrowthCore();
                    var amount = mutate(growth);
                    if (amount <= 0)
                    {
                        transaction.Rollback();
                        return false;
                    }
                    SaveUserGrowthCore(growth);
                    AddXpLogCore(source, amount, taskId, date);
                    transaction.Commit();
                    return true;
                }
                catch
                {
                    try { transaction.Rollback(); } catch { /* 已回滚或连接异常 */ }
                    throw;
                }
            });

        /// <summary>
        /// 追加一条经验流水。
        /// </summary>
        public void AddXpLog(string source, int amount, int? taskId, string date) => ExecuteLocked(() => AddXpLogCore(source, amount, taskId, date));

        private void AddXpLogCore(string source, int amount, int? taskId, string date)
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
        }

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
            // M20 修复：过滤已删任务的孤儿记录，避免虚高完成数/追溯误判全清
            cmd.CommandText = @"
                SELECT COUNT(DISTINCT d.TaskId) FROM DailyTaskCompletion d
                INNER JOIN Tasks t ON t.Id = d.TaskId AND t.IsDeleted = 0
                WHERE d.Date = @date
            ";
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
                    ("PomodoroSession", "IsDirty", "INTEGER DEFAULT 0"),
                    ("UserProfile", "LastComboSettledDate", "TEXT") // S9 修复：连击结算游标
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
                            // M3 修复：同 MigrateDatabase，迁移失败上抛而非静默
                            throw new InvalidOperationException($"数据库迁移失败：ALTER TABLE {table} ADD COLUMN {column}", ex);
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
            // S2 修复：先释放连接、再释放信号量（后台线程可能仍阻塞在 _dbLock.Wait()，
            // 先释放信号量会使其抛 ObjectDisposedException），最后清除单例引用
            _connection?.Dispose();
            _connection = null;
            _dbLock?.Dispose();
            _instance = null;
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
