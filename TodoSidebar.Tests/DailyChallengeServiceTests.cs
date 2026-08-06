using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Xunit;
using TodoSidebar.Models;
using TodoSidebar.Services;

namespace TodoSidebar.Tests
{
    /// <summary>
    /// DailyChallengeService 每日挑战测试（覆盖当天挑战数据，保证确定性）。
    /// </summary>
    public class DailyChallengeServiceTests
    {
        public DailyChallengeServiceTests()
        {
            Environment.SetEnvironmentVariable("TODOSIDEBAR_TEST_DB",
                Path.Combine(Path.GetTempPath(), "todosidebar_challenge_" + Guid.NewGuid().ToString("N") + ".db"));
        }

        [Fact]
        public void GetTodayChallenges_ShouldGenerateThree()
        {
            var challenges = DailyChallengeService.Instance.GetTodayChallenges();
            challenges.Count.Should().Be(3);
            challenges.Should().OnlyContain(c => c.Target > 0 && c.Xp > 0 && !string.IsNullOrEmpty(c.Title));
        }

        [Fact]
        public void GetTodayChallenges_ShouldBeStableSameDay()
        {
            var first = DailyChallengeService.Instance.GetTodayChallenges();
            var second = DailyChallengeService.Instance.GetTodayChallenges();
            first.Select(c => c.Type).Should().Equal(second.Select(c => c.Type));
        }

        [Fact]
        public void RegisterProgress_ShouldCompleteAndRewardOnce()
        {
            var today = DateTime.Today.ToString("yyyy-MM-dd");
            var db = DatabaseService.Instance;

            // 覆盖当天挑战为已知类型，保证测试确定性
            db.SaveDailyChallenges(today, new List<DailyChallenge>
            {
                new() { Date = today, Type = "complete_daily_tasks_3", Target = 3, Progress = 0, Completed = false }
            });

            var beforeXp = LevelService.Instance.GetGrowth().TotalXp;

            DailyChallengeService.Instance.RegisterProgress("complete_daily_tasks");
            DailyChallengeService.Instance.RegisterProgress("complete_daily_tasks");
            DailyChallengeService.Instance.RegisterProgress("complete_daily_tasks"); // 达标

            var after = db.GetDailyChallenges(today);
            after.Should().ContainSingle();
            after[0].Completed.Should().BeTrue();
            after[0].Progress.Should().Be(3);
            LevelService.Instance.GetGrowth().TotalXp.Should().Be(beforeXp + 20);

            // 防重：已完成挑战再次推进不再发 XP
            var xpAfterComplete = LevelService.Instance.GetGrowth().TotalXp;
            DailyChallengeService.Instance.RegisterProgress("complete_daily_tasks");
            LevelService.Instance.GetGrowth().TotalXp.Should().Be(xpAfterComplete);
        }
    }
}
