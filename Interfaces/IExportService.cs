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
        int ImportFromJson(string filePath);
        string CreateBackup();
        int RestoreBackup(string backupPath);
    }
}
