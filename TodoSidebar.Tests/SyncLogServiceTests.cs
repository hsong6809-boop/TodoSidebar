using FluentAssertions;
using Xunit;
using TodoSidebar.Services;

namespace TodoSidebar.Tests
{
    /// <summary>
    /// L16 修复（降级方案）：SyncLogService 为私有构造 + 单例，ExportToFile 硬编码
    /// %APPDATA%\TodoSidebar\sync_log.json，不支持自定义路径/实例注入，
    /// 测试只能写真实文件。故在构造时备份用户的 sync_log.json，Dispose 时还原；
    /// xUnit 为每个测试方法新建实例并保证调用 Dispose，断言失败也会还原。
    /// </summary>
    public class SyncLogServiceTests : IDisposable
    {
        private static readonly string LogPath = GetLogPath();
        private readonly string? _backupPath;   // 本次测试前的用户日志备份（不存在则为 null）
        private readonly bool _logExisted;      // 测试前用户日志是否存在（不存在则测试后删除生成物）

        private static string GetLogPath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return System.IO.Path.Combine(appData, "TodoSidebar", "sync_log.json");
        }

        public SyncLogServiceTests()
        {
            // L16：备份用户真实日志到带随机后缀的临时文件，避免与其他进程/测试冲突
            try
            {
                if (System.IO.File.Exists(LogPath))
                {
                    _logExisted = true;
                    _backupPath = LogPath + ".test_bak_" + Guid.NewGuid().ToString("N");
                    System.IO.File.Copy(LogPath, _backupPath, overwrite: true);
                }
            }
            catch
            {
                // 备份失败不阻断测试，仅放弃还原能力
            }
        }

        /// <summary>L16：测试结束还原用户原始 sync_log.json（try/finally 语义由 Dispose 保证）</summary>
        public void Dispose()
        {
            try
            {
                if (_logExisted && _backupPath != null && System.IO.File.Exists(_backupPath))
                {
                    System.IO.File.Copy(_backupPath, LogPath, overwrite: true);
                }
                else if (!_logExisted && System.IO.File.Exists(LogPath))
                {
                    // 测试前不存在 → 测试产生的日志文件直接删除，还原原状
                    System.IO.File.Delete(LogPath);
                }
            }
            catch
            {
                // 还原失败不影响测试结果
            }
            finally
            {
                // 清理临时备份文件
                try
                {
                    if (_backupPath != null && System.IO.File.Exists(_backupPath))
                        System.IO.File.Delete(_backupPath);
                }
                catch { }
            }
        }

        private SyncLogService GetService()
        {
            var service = SyncLogService.Instance;
            service.Clear(); // 每个测试前清空
            return service;
        }

        [Fact]
        public void AfterClear_ShouldHaveEmptyLog()
        {
            var service = GetService();
            service.GetAll().Should().BeEmpty();
        }

        [Fact]
        public void Log_ShouldAddEntry()
        {
            var service = GetService();
            service.Log(new SyncLogEntry { Action = "sync", Success = true });
            service.GetAll().Should().HaveCount(1);
        }

        [Fact]
        public void GetRecent_ShouldReturnLimitedEntries()
        {
            var service = GetService();
            for (int i = 0; i < 10; i++)
                service.Log(new SyncLogEntry { Action = "sync", Success = true });

            service.GetRecent(3).Should().HaveCount(3);
        }

        [Fact]
        public void GetErrors_ShouldReturnOnlyFailedEntries()
        {
            var service = GetService();
            service.Log(new SyncLogEntry { Action = "sync", Success = true });
            service.Log(new SyncLogEntry { Action = "sync", Success = false, ErrorMessage = "timeout" });
            service.Log(new SyncLogEntry { Action = "sync", Success = true, Errors = 2 });

            var errors = service.GetErrors();
            errors.Should().HaveCount(2);
        }

        [Fact]
        public void GetSummary_ShouldCalculateCorrectly()
        {
            var service = GetService();
            service.Log(new SyncLogEntry { Action = "sync", Success = true, Uploaded = 5, Downloaded = 3 });
            service.Log(new SyncLogEntry { Action = "sync", Success = false, ErrorMessage = "error" });
            service.Log(new SyncLogEntry { Action = "sync", Success = true, Uploaded = 2, Downloaded = 1, Conflicts = 1 });

            var summary = service.GetSummary();
            summary.TotalSyncs.Should().Be(3);
            summary.SuccessfulSyncs.Should().Be(2);
            summary.FailedSyncs.Should().Be(1);
            summary.TotalUploaded.Should().Be(7);
            summary.TotalDownloaded.Should().Be(4);
            summary.TotalConflicts.Should().Be(1);
            summary.LastError.Should().Be("error");
        }

        [Fact]
        public void MaxEntries_ShouldEvictOldest()
        {
            var service = GetService();
            for (int i = 0; i < 110; i++)
                service.Log(new SyncLogEntry { Action = "sync", Success = true, Details = $"entry {i}" });

            service.GetAll().Should().HaveCount(100);
            service.GetRecent(1).First().Details.Should().Be("entry 109");
        }

        [Fact]
        public void ExportToFile_ShouldReturnValidPath()
        {
            var service = GetService();
            service.Log(new SyncLogEntry { Action = "sync", Success = true });
            var path = service.ExportToFile();
            path.Should().NotBeNullOrEmpty();
            System.IO.File.Exists(path).Should().BeTrue();
        }
    }
}
