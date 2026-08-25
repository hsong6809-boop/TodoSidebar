using System.Collections.Generic;
using TodoSidebar.Models;

namespace TodoSidebar.Services
{
    /// <summary>
    /// 导出服务接口。
    /// </summary>
    public interface IExportService
    {
        void ExportToJson(string filePath);
        void ExportToCsv(string filePath);

        /// <summary>v5.3：导出 Markdown（Obsidian/Notion 友好）。</summary>
        void ExportToMarkdown(string filePath);

        int ImportFromJson(string filePath);
        string CreateBackup();
        int RestoreBackup(string backupPath);

        /// <summary>获取备份列表</summary>
        List<BackupInfo> GetBackupList();
    }
}
