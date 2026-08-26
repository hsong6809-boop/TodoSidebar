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
                // 原子写：先写临时文件再替换，避免导出中途崩溃损坏文件。
                // R53 修复（审查 L3）：GUID 临时名（固定 .tmp 名在同路径并发导出时互踩）
                // + 失败清理临时文件（原实现异常路径残留孤儿文件）
                var tempPath = filePath + $".{Guid.NewGuid():N}.tmp";
                try
                {
                    File.WriteAllText(tempPath, json);
                    File.Move(tempPath, filePath, overwrite: true);
                }
                catch
                {
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* 尽力清理 */ }
                    throw;
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"导出 JSON 失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// v5.3：导出为 Markdown（Obsidian / Notion 友好）。
        /// 已完成任务按完成日期分组 `- [x]`；未完成任务单列一节。
        /// </summary>
        public void ExportToMarkdown(string filePath)
        {
            try
            {
                var tasks = _dbService.GetTasks();

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"# 每日任务导出 · {DateTime.Now:yyyy-MM-dd HH:mm}");
                sb.AppendLine();
                sb.AppendLine($"> 共 {tasks.Count} 个任务，其中已完成 {tasks.Count(t => t.IsCompleted)} 个。由 TodoSidebar 导出。");
                sb.AppendLine();

                // 已完成：按完成日期倒序分组。
                // R54 修复（审查 L1）：不再丢弃"已完成但无完成时间"的行——
                // 原实现两个集合都不含 IsCompleted && !CompletedAt，导出静默少数据
                var completed = tasks
                    .Where(t => t.IsCompleted && t.CompletedAt.HasValue)
                    .OrderByDescending(t => t.CompletedAt)
                    .ToList();
                var completedNoTime = tasks
                    .Where(t => t.IsCompleted && !t.CompletedAt.HasValue)
                    .ToList();
                if (completed.Count > 0 || completedNoTime.Count > 0)
                {
                    sb.AppendLine("## ✅ 已完成");
                    sb.AppendLine();
                    foreach (var group in completed.GroupBy(t => t.CompletedAt!.Value.Date).OrderByDescending(g => g.Key))
                    {
                        sb.AppendLine($"### {group.Key:yyyy-MM-dd} 周{GetWeekdayCn(group.Key)}");
                        sb.AppendLine();
                        foreach (var t in group)
                            sb.AppendLine($"- [x] {EscapeMarkdown(t.Title)}{FormatTaskSuffix(t)} · {t.CompletedAt:HH:mm}");
                        sb.AppendLine();
                    }
                    if (completedNoTime.Count > 0)
                    {
                        foreach (var t in completedNoTime)
                            sb.AppendLine($"- [x] {EscapeMarkdown(t.Title)}{FormatTaskSuffix(t)} · 完成时间未知");
                        sb.AppendLine();
                    }
                }

                // 未完成
                var pending = tasks.Where(t => !t.IsCompleted).ToList();
                if (pending.Count > 0)
                {
                    sb.AppendLine("## 🕙 进行中");
                    sb.AppendLine();
                    foreach (var t in pending.OrderBy(t => t.Type).ThenBy(t => t.Deadline ?? DateTime.MaxValue))
                        sb.AppendLine($"- [ ] {EscapeMarkdown(t.Title)}{FormatTaskSuffix(t)}");
                    sb.AppendLine();
                }

                // 原子写 + UTF-8 BOM（编辑器兼容性最好）+ 失败清理（R53，审查 L3）
                var tempPath = filePath + $".{Guid.NewGuid():N}.tmp";
                try
                {
                    using (var writer = new StreamWriter(tempPath, false, new System.Text.UTF8Encoding(true)))
                    {
                        writer.Write(sb.ToString());
                    }
                    File.Move(tempPath, filePath, overwrite: true);
                }
                catch
                {
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* 尽力清理 */ }
                    throw;
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"导出 Markdown 失败: {ex.Message}", ex);
            }
        }

        private static string FormatTaskSuffix(TaskItem t)
        {
            var parts = new List<string>();
            if (t.Type == TaskType.Deadline && t.Deadline.HasValue)
                parts.Add($"截止 {t.Deadline.Value:MM-dd HH:mm}");
            if (!string.IsNullOrWhiteSpace(t.Tags))
                parts.Add(string.Join(" ", t.Tags.Split('#', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => "#" + x.Trim())));
            return parts.Count > 0 ? " （" + string.Join(" · ", parts) + "）" : "";
        }

        /// <summary>
        /// Markdown 转义。R55 修复（审查 L2）：反斜杠必须最先转义；剔除换行防止撕裂列表项；
        /// 转义反引号防止在 Obsidian 等渲染器中形成代码围栏注入。
        /// </summary>
        private static string EscapeMarkdown(string text)
            => text.Replace("\\", "\\\\", StringComparison.Ordinal)
                   .Replace("[", "\\[", StringComparison.Ordinal)
                   .Replace("]", "\\]", StringComparison.Ordinal)
                   .Replace("`", "\\`", StringComparison.Ordinal)
                   .Replace("\r", " ", StringComparison.Ordinal)
                   .Replace("\n", " ", StringComparison.Ordinal);

        private static string GetWeekdayCn(DateTime date)
            => "日一二三四五六"[(int)date.DayOfWeek].ToString();

        // 导出为 CSV（修复转义问题）
        public void ExportToCsv(string filePath)
        {            try
            {
                var tasks = _dbService.GetTasks();

                // M5 修复：UTF-8 带 BOM，Excel 直接打开中文不乱码；临时文件+原子替换防截断
                var tempPath = filePath + $".{Guid.NewGuid():N}.tmp";
                try
                {
                    using (var writer = new StreamWriter(tempPath, false, new System.Text.UTF8Encoding(true)))
                    {
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
                    File.Move(tempPath, filePath, overwrite: true);
                }
                finally
                {
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
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

            // M6 修复：以 = + - @ 开头的字段前置单引号，防止 Excel 将其当公式执行（CSV 注入）
            if (field[0] is '=' or '+' or '-' or '@')
            {
                field = "'" + field;
            }

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

                // M4 修复：单事务导入 + 按 SyncId 去重（失败整体回滚，重复导入不再翻倍）
                var validTasks = importData.Tasks
                    .Where(t => !string.IsNullOrWhiteSpace(t.Title))
                    .ToList();
                int importedCount = _dbService.ImportTasksUnique(validTasks);

                // 导入设置（仅导入非敏感设置）
                if (importData.Settings != null)
                {
                    var safeKeys = new[] { "Theme", "Accent", "FontSize", "LastWeeklyReset" };
                    foreach (var key in safeKeys)
                    {
                        if (importData.Settings.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
                            _dbService.SetSetting(key, value);
                    }

                    // R56 修复（审查 L4）：恢复外观设置后走 ThemeManager 单一入口应用——
                    // 原实现只写 Settings 表，运行时界面不变、重启才跳变，
                    // 极易被误报为 V5.1"强调色重启回弹"复发
                    ApplyRestoredAppearance();
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

        // 恢复备份（替换模式：事务内软删现有数据 + 导入，失败整体回滚，避免数据丢失）
        public int RestoreBackup(string backupPath)
        {
            // 1. 先解析备份文件（解析失败则直接报错，不触碰现有数据）
            var json = File.ReadAllText(backupPath);
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var importData = JsonSerializer.Deserialize<ExportData>(json, options);
            if (importData?.Tasks == null)
                throw new InvalidOperationException("备份文件格式无效或没有任务数据");

            var tasks = importData.Tasks.Where(t => !string.IsNullOrWhiteSpace(t.Title)).ToList();
            // S3 修复：保留备份中的原始 Id（ReplaceAllTasks 已改为物理删除 + 按 Id 插入，
            // 子表 TaskId 引用在同源恢复场景下保持有效）

            // 2. 单事务替换：物理删除现有 + 按原始 Id 插入 + 清理子表孤儿
            _dbService.ReplaceAllTasks(tasks);

            // 3. 导入设置（仅导入非敏感设置）
            if (importData.Settings != null)
            {
                var safeKeys = new[] { "Theme", "Accent", "FontSize", "LastWeeklyReset" };
                foreach (var key in safeKeys)
                {
                    if (importData.Settings.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
                        _dbService.SetSetting(key, value);
                }

                // R56 修复（审查 L4）：同 ImportFromJson——恢复后立即应用外观
                ApplyRestoredAppearance();
            }

            return tasks.Count;
        }

        /// <summary>
        /// R56：把已写入 Settings 的 Theme/Accent 通过 ThemeManager 应用到运行中的界面。
        /// setter 内部有同名短路，值未变化时不会产生多余重绘。
        /// </summary>
        private static void ApplyRestoredAppearance()
        {
            try
            {
                var tm = ThemeManager.Instance;
                var db = DatabaseService.Instance;

                var accent = db.GetSetting("Accent");
                if (!string.IsNullOrEmpty(accent) && !string.Equals(tm.CurrentAccent, accent, StringComparison.OrdinalIgnoreCase))
                    tm.CurrentAccent = accent;

                var theme = db.GetSetting("Theme");
                if (!string.IsNullOrEmpty(theme) && Enum.TryParse<ThemeType>(theme, ignoreCase: true, out var parsed))
                    tm.ApplyTheme(parsed);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ExportService: 应用恢复的外观设置失败: {ex.Message}");
            }
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
            var keys = new[] { "Theme", "LastWeeklyReset", "Accent", "FontSize" };

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
