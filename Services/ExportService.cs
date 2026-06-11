using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TodoSidebar.Models;

namespace TodoSidebar.Services
{
    public class ExportService : IExportService
    {
        private readonly DatabaseService _dbService;
        private readonly TaskService _taskService;

        public ExportService(DatabaseService dbService)
        {
            _dbService = dbService;
            _taskService = new TaskService(dbService);
        }

        // 导出为 JSON
        public void ExportToJson(string filePath)
        {
            try
            {
                var exportData = new ExportData
                {
                    ExportDate = DateTime.Now,
                    Tasks = _dbService.GetTasks(),
                    Settings = GetAllSettings()
                };

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var json = JsonSerializer.Serialize(exportData, options);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"导出 JSON 失败: {ex.Message}", ex);
            }
        }

        // 导出为 CSV（修复转义问题）
        public void ExportToCsv(string filePath)
        {
            try
            {
                var tasks = _dbService.GetTasks();
            using var writer = new StreamWriter(filePath);

            // 写入表头
            writer.WriteLine("Id,Title,Type,Priority,IsCompleted,CreatedAt,Deadline,CompletedAt,Tags");

            // 写入数据
            foreach (var task in tasks)
            {
                writer.WriteLine(string.Join(",",
                    task.Id,
                    EscapeCsvField(task.Title),
                    task.Type switch { TaskType.Daily => "每日", TaskType.Deadline => "截止", _ => task.Type.ToString() },
                    task.Priority switch { TaskPriority.High => "高", TaskPriority.Medium => "中", TaskPriority.Low => "低", _ => task.Priority.ToString() },
                    task.IsCompleted,
                    task.CreatedAt.ToString("O"),
                    task.Deadline?.ToString("O") ?? "",
                    task.CompletedAt?.ToString("O") ?? "",
                    EscapeCsvField(task.Tags ?? "")
                ));
            }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"导出 CSV 失败: {ex.Message}", ex);
            }
        }

        // CSV 字段转义
        private static string EscapeCsvField(string field)
        {
            if (string.IsNullOrEmpty(field)) return "\"\"";
            
            // 如果包含逗号、引号、换行符，需要转义
            if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
            {
                // 双引号转义为两个双引号
                return "\"" + field.Replace("\"", "\"\"") + "\"";
            }
            return "\"" + field + "\"";
        }

        // 从 JSON 导入
        public int ImportFromJson(string filePath)
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var importData = JsonSerializer.Deserialize<ExportData>(json, options);
                if (importData?.Tasks == null) return 0;

                int importedCount = 0;

                // 导入任务（跳过无效任务）
                foreach (var task in importData.Tasks)
                {
                    if (string.IsNullOrWhiteSpace(task.Title))
                        continue;
                    task.Id = 0;
                    _dbService.InsertTask(task);
                    importedCount++;
                }

                // 导入设置（仅导入非敏感设置）
                if (importData.Settings != null)
                {
                    var safeKeys = new[] { "Theme", "AccentColor", "FontSize", "LastWeeklyReset" };
                    foreach (var key in safeKeys)
                    {
                        if (importData.Settings.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
                            _dbService.SetSetting(key, value);
                    }
                }

                return importedCount;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"导入 JSON 失败: {ex.Message}", ex);
            }
        }

        // 备份数据
        public string CreateBackup()
        {
            var backupDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TodoSidebar", "Backups");

            Directory.CreateDirectory(backupDir);

            var backupFileName = $"backup_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            var backupPath = Path.Combine(backupDir, backupFileName);

            ExportToJson(backupPath);

            // 清理旧备份（保留最近10个）
            CleanOldBackups(backupDir, 10);

            return backupPath;
        }

        // 恢复备份（替换模式：先清除现有数据再导入）
        public int RestoreBackup(string backupPath)
        {
            // 软删除所有现有任务（标记 IsDeleted，同步时会同步到云端）
            var allTasks = _dbService.GetTasks();
            foreach (var task in allTasks)
            {
                _dbService.DeleteTask(task.Id);
            }
            return ImportFromJson(backupPath);
        }

        // 获取备份列表
        public List<BackupInfo> GetBackupList()
        {
            var backupDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TodoSidebar", "Backups");

            var backups = new List<BackupInfo>();

            if (Directory.Exists(backupDir))
            {
                foreach (var file in Directory.GetFiles(backupDir, "backup_*.json"))
                {
                    var fileInfo = new FileInfo(file);
                    backups.Add(new BackupInfo
                    {
                        FilePath = file,
                        FileName = fileInfo.Name,
                        CreatedDate = fileInfo.CreationTime,
                        Size = fileInfo.Length
                    });
                }
            }

            return backups;
        }

        private void CleanOldBackups(string backupDir, int keepCount)
        {
            var files = Directory.GetFiles(backupDir, "backup_*.json");
            if (files.Length <= keepCount) return;

            Array.Sort(files, (a, b) => File.GetCreationTime(b).CompareTo(File.GetCreationTime(a)));

            for (int i = keepCount; i < files.Length; i++)
            {
                File.Delete(files[i]);
            }
        }

        private Dictionary<string, string> GetAllSettings()
        {
            var settings = new Dictionary<string, string>();
            // 获取常用设置
            var keys = new[] { "Theme", "LastWeeklyReset", "AccentColor", "FontSize" };

            foreach (var key in keys)
            {
                var value = _dbService.GetSetting(key);
                if (value != null)
                {
                    settings[key] = value;
                }
            }

            return settings;
        }
    }

    public class ExportData
    {
        public DateTime ExportDate { get; set; }
        public List<TaskItem> Tasks { get; set; } = new();
        public Dictionary<string, string> Settings { get; set; } = new();
    }

    public class BackupInfo
    {
        public string FilePath { get; set; } = "";
        public string FileName { get; set; } = "";
        public DateTime CreatedDate { get; set; }
        public long Size { get; set; }
    }
}
