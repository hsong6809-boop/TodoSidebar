using FluentAssertions;
using Xunit;
using TodoSidebar.Services;

namespace TodoSidebar.Tests
{
    public class SyncLogServiceTests
    {
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
