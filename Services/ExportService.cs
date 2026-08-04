using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        // 修复 F5：导出为 Markdown
        public void ExportToMarkdown(string filePath)
        {
            try
            {
                var tasks = _dbService.GetTasks();
                using var writer = new StreamWriter(filePath);

                writer.WriteLine("# TodoSidebar 任务导出");
                writer.WriteLine();
                writer.WriteLine($"> 导出时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}  |  共 {tasks.Count} 条任务");
                writer.WriteLine();

                var dailyTasks = tasks.Where(t => t.Type == TaskType.Daily).ToList();
                var deadlineTasks = tasks.Where(t => t.Type == TaskType.Deadline).ToList();

                if (dailyTasks.Count > 0)
                {
                    writer.WriteLine("## 📋 每日任务");
                    writer.WriteLine();
                    foreach (var task in dailyTasks)
                    {
                        var status = task.IsCompleted ? "✅" : "⬜";
                        var priorityIcon = task.Priority switch
                        {
                            TaskPriority.High => "🔴 高",
                            TaskPriority.Medium => "🟡 中",
                            TaskPriority.Low => "🟢 低",
                            _ => "⚪"
                        };
                        writer.WriteLine($"- {status} **{task.Title}**（{priorityIcon}）");
                        if (!string.IsNullOrWhiteSpace(task.Description))
                            writer.WriteLine($"  - {task.Description.Replace("\n", " ")}");
                        if (!string.IsNullOrWhiteSpace(task.Tags))
                            writer.WriteLine($"  - 标签：{task.Tags}");
                        if (task.HasSubTasks)
                        {
                            writer.WriteLine($"  - 子任务：{task.SubTasksProgressText}");
                            foreach (var sub in task.SubTasksList)
                            {
                                var subIcon = sub.IsCompleted ? "✅" : "⬜";
                                writer.WriteLine($"    - {subIcon} {sub.Title}");
                            }
                        }
                    }
                    writer.WriteLine();
                }

                if (deadlineTasks.Count > 0)
                {
                    writer.WriteLine("## ⏰ 截止任务");
                    writer.WriteLine();
                    foreach (var task in deadlineTasks.OrderBy(t => t.Deadline))
                    {
                        var status = task.IsCompleted ? "✅" : "⬜";
                        var priorityIcon = task.Priority switch
                        {
                            TaskPriority.High => "🔴 高",
                            TaskPriority.Medium => "🟡 中",
                            TaskPriority.Low => "🟢 低",
                            _ => "⚪"
                        };
                        var deadlineStr = task.Deadline?.ToString("yyyy-MM-dd HH:mm") ?? "无截止日期";
                        writer.WriteLine($"- {status} **{task.Title}**（{priorityIcon}）— 📅 {deadlineStr}");
                        if (!string.IsNullOrWhiteSpace(task.Description))
                            writer.WriteLine($"  - {task.Description.Replace("\n", " ")}");
                        if (!string.IsNullOrWhiteSpace(task.Tags))
                            writer.WriteLine($"  - 标签：{task.Tags}");
                        if (task.HasSubTasks)
                        {
                            writer.WriteLine($"  - 子任务：{task.SubTasksProgressText}");
                            foreach (var sub in task.SubTasksList)
                            {
                                var subIcon = sub.IsCompleted ? "✅" : "⬜";
                                writer.WriteLine($"    - {subIcon} {sub.Title}");
                            }
                        }
                    }
                    writer.WriteLine();
                }

                writer.WriteLine("---");
                writer.WriteLine($"*由 TodoSidebar v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)} 导出*");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"导出 Markdown 失败: {ex.Message}", ex);
            }
        }

        // 修复 F5：从 CSV 导入（兼容导出的格式：Id,Title,Type,Priority,IsCompleted,...）
        public int ImportFromCsv(string filePath)
        {
            try
            {
                int importedCount = 0;
                var lines = File.ReadAllLines(filePath);
                if (lines.Length <= 1) return 0; // 只有表头或空文件

                // 解析表头
                var header = ParseCsvLine(lines[0]);
                var colIndex = new Dictionary<string, int>();
                for (int i = 0; i < header.Count; i++)
                    colIndex[header[i].Trim()] = i;

                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    var fields = ParseCsvLine(lines[i]);
                    if (fields.Count < 2) continue;

                    var title = fields[colIndex.ContainsKey("Title") ? colIndex["Title"] : 1].Trim();
                    if (string.IsNullOrWhiteSpace(title)) continue;

                    var task = new TaskItem { Title = title };

                    if (colIndex.ContainsKey("Type"))
                    {
                        var typeStr = fields[colIndex["Type"]].Trim();
                        task.Type = typeStr switch
                        {
                            "每日" or "Daily" or "0" => TaskType.Daily,
                            "截止" or "Deadline" or "1" => TaskType.Deadline,
                            _ => TaskType.Daily
                        };
                    }

                    if (colIndex.ContainsKey("Priority"))
                    {
                        var prioStr = fields[colIndex["Priority"]].Trim();
                        task.Priority = prioStr switch
                        {
                            "高" or "High" or "2" => TaskPriority.High,
                            "低" or "Low" or "0" => TaskPriority.Low,
                            _ => TaskPriority.Medium
                        };
                    }

                    if (colIndex.ContainsKey("IsCompleted") && fields[colIndex["IsCompleted"]].Trim() == "True")
                        task.IsCompleted = true;

                    if (colIndex.ContainsKey("Deadline") && DateTime.TryParse(fields[colIndex["Deadline"]], out var deadline))
                        task.Deadline = deadline;

                    if (colIndex.ContainsKey("CompletedAt") && DateTime.TryParse(fields[colIndex["CompletedAt"]], out var completedAt))
                        task.CompletedAt = completedAt;

                    if (colIndex.ContainsKey("CreatedAt") && DateTime.TryParse(fields[colIndex["CreatedAt"]], out var createdAt))
                        task.CreatedAt = createdAt;

                    if (colIndex.ContainsKey("Tags"))
                        task.Tags = fields[colIndex["Tags"]].Trim();

                    _dbService.InsertTask(task);
                    importedCount++;
                }

                return importedCount;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"导入 CSV 失败: {ex.Message}", ex);
            }
        }

        // 解析一行 CSV（支持引号包裹字段）
        private static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            var current = new System.Text.StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            current.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
                else
                {
                    if (c == '"')
                        inQuotes = true;
                    else if (c == ',')
                    {
                        result.Add(current.ToString());
                        current.Clear();
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
            }
            result.Add(current.ToString());
            return result;
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
            // 修复 B4：恢复备份时如果已登录且启用了云同步，
            // 软删除现有任务会在下次同步时把"删除"推送到云端，导致云端数据清空！
            // 这里改为：未登录/未启用同步时直接恢复；已登录时提示风险。
            if (AuthService.Instance.IsLoggedIn)
            {
                var proceed = System.Windows.MessageBox.Show(
                    "⚠️ 检测到已登录云同步账号。\n\n" +
                    "恢复备份会软删除当前所有任务，下次云同步时会把删除操作同步到云端，\n" +
                    "**可能清空云端数据**。\n\n" +
                    "建议：先在设置里手动同步（上传当前数据），再执行恢复。\n\n" +
                    "确定要继续吗？",
                    "恢复备份 - 风险提示",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);
                if (proceed != System.Windows.MessageBoxResult.Yes)
                    return 0;
            }

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
