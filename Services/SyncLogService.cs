using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TodoSidebar.Services
{
    /// <summary>
    /// 同步日志条目
    /// </summary>
    public class SyncLogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Action { get; set; } = "";        // "sync", "upload", "download", "conflict", "error"
        public bool Success { get; set; }
        public string? Details { get; set; }
        public int Uploaded { get; set; }
        public int Downloaded { get; set; }
        public int Conflicts { get; set; }
        public int Errors { get; set; }
        public string? ErrorMessage { get; set; }
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// 同步日志服务。
    /// 记录最近 100 条同步操作，支持导出用于诊断。
    /// </summary>
    public class SyncLogService
    {
        private static SyncLogService? _instance;
        public static SyncLogService Instance => _instance ??= new SyncLogService();

        private readonly List<SyncLogEntry> _log = new();
        private readonly object _lock = new();
        private const int MaxEntries = 100;
        private const string LogFileName = "sync_log.json";

        private SyncLogService() { }

        /// <summary>
        /// 记录一次同步操作
        /// </summary>
        public void Log(SyncLogEntry entry)
        {
            lock (_lock)
            {
                _log.Add(entry);
                if (_log.Count > MaxEntries)
                    _log.RemoveAt(0);
            }
        }

        /// <summary>
        /// 获取最近 N 条日志
        /// </summary>
        public List<SyncLogEntry> GetRecent(int count = 20)
        {
            lock (_lock)
            {
                return _log.TakeLast(count).ToList();
            }
        }

        /// <summary>
        /// 获取所有日志
        /// </summary>
        public List<SyncLogEntry> GetAll()
        {
            lock (_lock)
            {
                return new List<SyncLogEntry>(_log);
            }
        }

        /// <summary>
        /// 获取失败的同步记录
        /// </summary>
        public List<SyncLogEntry> GetErrors()
        {
            lock (_lock)
            {
                return _log.Where(e => !e.Success || e.Errors > 0).ToList();
            }
        }

        /// <summary>
        /// 清除所有日志
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _log.Clear();
            }
        }

        /// <summary>
        /// 导出日志到文件（用于发送给开发者诊断）
        /// </summary>
        public string ExportToFile()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "TodoSidebar");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, LogFileName);

            List<SyncLogEntry> snapshot;
            lock (_lock)
            {
                snapshot = new List<SyncLogEntry>(_log);
            }

            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
            return path;
        }

        /// <summary>
        /// 统计摘要
        /// </summary>
        public SyncLogSummary GetSummary()
        {
            lock (_lock)
            {
                return new SyncLogSummary
                {
                    TotalSyncs = _log.Count(e => e.Action == "sync"),
                    SuccessfulSyncs = _log.Count(e => e.Action == "sync" && e.Success),
                    FailedSyncs = _log.Count(e => e.Action == "sync" && !e.Success),
                    TotalUploaded = _log.Sum(e => e.Uploaded),
                    TotalDownloaded = _log.Sum(e => e.Downloaded),
                    TotalConflicts = _log.Sum(e => e.Conflicts),
                    LastSyncTime = _log.LastOrDefault(e => e.Action == "sync")?.Timestamp,
                    LastError = _log.LastOrDefault(e => !e.Success)?.ErrorMessage
                };
            }
        }
    }

    public class SyncLogSummary
    {
        public int TotalSyncs { get; set; }
        public int SuccessfulSyncs { get; set; }
        public int FailedSyncs { get; set; }
        public int TotalUploaded { get; set; }
        public int TotalDownloaded { get; set; }
        public int TotalConflicts { get; set; }
        public DateTime? LastSyncTime { get; set; }
        public string? LastError { get; set; }
    }
}
