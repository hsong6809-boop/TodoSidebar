using System;
using System.IO;
using FluentAssertions;
using Xunit;
using TodoSidebar.Models;
using TodoSidebar.Services;

namespace TodoSidebar.Tests
{
    /// <summary>
    /// LevelService 升级系统测试。
    /// 通过环境变量将 DatabaseService 指向临时数据库，避免污染真实用户数据。
    /// </summary>
    public class LevelServiceTests
    {
        private readonly string _testDbPath;

        public LevelServiceTests()
        {
            // 在首次访问 DatabaseService.Instance 前设置测试数据库路径
            _testDbPath = Path.Combine(Path.GetTempPath(), "todosidebar_test_" + Guid.NewGuid().ToString("N") + ".db");
            Environment.SetEnvironmentVariable("TODOSIDEBAR_TEST_DB", _testDbPath);
        }

        [Fact]
        public void XpForNextLevel_ShouldFollowFormula()
        {
            LevelService.XpForNextLevel(1).Should().Be(100);
            LevelService.XpForNextLevel(2).Should().Be(120);
            LevelService.XpForNextLevel(10).Should().Be(280);
            LevelService.XpForNextLevel(30).Should().Be(680);
        }

        [Fact]
        public void TitleForLevel_ShouldMapRanges()
        {
            LevelService.TitleForLevel(1).Should().Be("初出茅庐");
            LevelService.TitleForLevel(5).Should().Be("勤勉学徒");
            LevelService.TitleForLevel(10).Should().Be("专注修行者");
            LevelService.TitleForLevel(30).Should().Be("任务大师");
            LevelService.TitleForLevel(50).Should().Be("效率贤者");
            LevelService.TitleForLevel(99).Should().Be("传说冒险者");
        }

        [Fact]
        public void Reward_ShouldLevelUpAndKeepRemainder()
        {
            // 测试共享单例状态，使用相对断言：基于奖励前状态模拟期望的升级结果
            var service = LevelService.Instance;
            var before = service.GetGrowth();

            var leveledUp = false;
            service.LevelUp += (s, e) => { leveledUp = true; };

            service.Reward("task_complete", 250, taskId: 777001);

            var after = service.GetGrowth();
            // 模拟：XP 累加 250 后按公式逐级进位
            int expectedLevel = before.Level;
            int expectedXp = before.Xp + 250;
            while (expectedXp >= LevelService.XpForNextLevel(expectedLevel))
            {
                expectedXp -= LevelService.XpForNextLevel(expectedLevel);
                expectedLevel++;
            }

            after.Level.Should().Be(expectedLevel);
            after.Xp.Should().Be(expectedXp);
            after.TotalXp.Should().Be(before.TotalXp + 250);
            after.Title.Should().Be(LevelService.TitleForLevel(expectedLevel));
            leveledUp.Should().Be(expectedLevel > before.Level);
        }

        [Fact]
        public void Reward_SameTaskSameDay_ShouldNotDoubleCount()
        {
            var service = LevelService.Instance;
            service.Reward("task_complete", 10, taskId: 999001);
            var afterFirst = service.GetGrowth().TotalXp;

            // 同来源 + 同任务 + 同一天 → 防重，不再叠加
            service.Reward("task_complete", 10, taskId: 999001);
            service.GetGrowth().TotalXp.Should().Be(afterFirst);
        }

        [Fact]
        public void Reward_NonPositiveAmount_ShouldBeIgnored()
        {
            var service = LevelService.Instance;
            var before = service.GetGrowth().TotalXp;
            service.Reward("task_complete", 0, taskId: 999002);
            service.Reward("task_complete", -5, taskId: 999003);
            service.GetGrowth().TotalXp.Should().Be(before);
        }

        [Fact]
        public void SettleCombo_ShouldBeIdempotentSameDay()
        {
            // 无论当天连击如何结算，同一天重复调用不应重复加 XP
            var service = LevelService.Instance;
            service.SettleCombo();
            var afterFirst = service.GetGrowth().TotalXp;
            service.SettleCombo();
            var afterSecond = service.GetGrowth().TotalXp;
            afterSecond.Should().Be(afterFirst);
        }

        [Fact]
        public void SettleCombo_FullClearYesterday_ShouldGrantComboXp()
        {
            var service = LevelService.Instance;
            var db = DatabaseService.Instance;
            var beforeCombo = service.GetGrowth().ComboDays;
            var beforeXp = service.GetGrowth().TotalXp;

            // 插入一个每日任务，并登记昨天完成（确保昨天全清判定通过）
            var task = new TaskItem { Title = "combo_ut_" + Guid.NewGuid().ToString("N"), Type = TaskType.Daily };
            var taskId = db.InsertTask(task);
            db.MarkDailyTaskCompleted(taskId, DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd"));

            service.SettleCombo();

            var after = service.GetGrowth();
            after.ComboDays.Should().BeGreaterThanOrEqualTo(beforeCombo + 1);
            after.TotalXp.Should().BeGreaterThanOrEqualTo(beforeXp + 2);
        }

        [Fact]
        public void DeriveFromTotal_ShouldRebuildLevelAndXp()
        {
            // 跨设备合并时用 TotalXp 重算等级
            LevelService.DeriveFromTotal(0).Should().Be((1, 0));
            LevelService.DeriveFromTotal(99).Should().Be((1, 99));
            LevelService.DeriveFromTotal(100).Should().Be((2, 0));   // Lv1 需 100
            LevelService.DeriveFromTotal(250).Should().Be((3, 30));  // 100+120 后剩 30
        }

        [Fact]
        public void GetDailyXpLastDays_ShouldIncludeTodayReward()
        {
            var db = DatabaseService.Instance;
            LevelService.Instance.Reward("task_complete", 10, taskId: 910001);

            var data = db.GetDailyXpLastDays(7);
            data.Count.Should().Be(7);
            data.Should().Contain(d => d.date == DateTime.Today && d.xp >= 10);
            // 日期有序且无缺失
            for (int i = 1; i < data.Count; i++)
                (data[i].date - data[i - 1].date).Days.Should().Be(1);
        }
    }
}
