using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;
using TodoSidebar.Services;

namespace TodoSidebar.Tests
{
    /// <summary>
    /// AchievementService 成就徽章测试（相对断言，避免共享单例状态干扰）。
    /// </summary>
    public class AchievementServiceTests
    {
        public AchievementServiceTests()
        {
            Environment.SetEnvironmentVariable("TODOSIDEBAR_TEST_DB",
                Path.Combine(Path.GetTempPath(), "todosidebar_ach_" + Guid.NewGuid().ToString("N") + ".db"));
        }

        [Fact]
        public void CheckAll_WithTaskReward_ShouldUnlockFirstTaskBadge()
        {
            // 造一条任务完成流水 → 满足 task_1（新手上路）
            LevelService.Instance.Reward("task_complete", 10, taskId: 900001);
            AchievementService.Instance.CheckAll();

            DatabaseService.Instance.GetUnlockedAchievements().Should().Contain("task_1");
        }

        [Fact]
        public void CheckAll_RepeatedCalls_ShouldBeIdempotent()
        {
            var db = DatabaseService.Instance;
            AchievementService.Instance.CheckAll();
            var mid = db.GetUnlockedAchievements().Count;
            AchievementService.Instance.CheckAll();
            var after = db.GetUnlockedAchievements().Count;

            // 已解锁徽章不会重复新增
            after.Should().Be(mid);
        }

        [Fact]
        public void Definitions_ShouldContainThirtyFiveBadges()
        {
            // v5.5 扩充：首批 20 枚 + 第二批 15 枚
            AchievementService.Instance.GetDefinitions().Count.Should().Be(35);
        }

        [Fact]
        public void Definitions_ShouldHaveUniqueIds()
        {
            var ids = AchievementService.Instance.GetDefinitions().Select(d => d.Id).ToList();
            ids.Count.Should().Be(ids.Distinct().Count(), "徽章 Id 必须唯一");
        }
    }
}
