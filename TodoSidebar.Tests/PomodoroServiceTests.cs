using System;
using System.IO;
using FluentAssertions;
using Xunit;
using TodoSidebar.Services;

namespace TodoSidebar.Tests
{
    /// <summary>
    /// PomodoroService 番茄钟测试。
    /// 断言均用相对值，避免与其他测试类共享单例状态产生干扰。
    /// </summary>
    public class PomodoroServiceTests
    {
        public PomodoroServiceTests()
        {
            // 与 LevelServiceTests 相同的隔离策略（单例已建时环境变量不生效，故全部使用相对断言）
            Environment.SetEnvironmentVariable("TODOSIDEBAR_TEST_DB",
                Path.Combine(Path.GetTempPath(), "todosidebar_pomo_" + Guid.NewGuid().ToString("N") + ".db"));
        }

        [Fact]
        public void FormatTime_ShouldFormatMinutesSeconds()
        {
            PomodoroService.FormatTime(0).Should().Be("00:00");
            PomodoroService.FormatTime(59).Should().Be("00:59");
            PomodoroService.FormatTime(1500).Should().Be("25:00");
        }

        [Fact]
        public void Start_ShouldEnterFocusState()
        {
            var pomo = PomodoroService.Instance;
            pomo.Start(null, "", 25);
            pomo.State.Should().Be(PomodoroState.Focus);
            pomo.TotalSeconds.Should().Be(25 * 60);
            pomo.Stop(true); // 清理
        }

        [Fact]
        public void PauseResume_ShouldToggleState()
        {
            var pomo = PomodoroService.Instance;
            pomo.Start(null, "", 25);
            pomo.Pause();
            pomo.State.Should().Be(PomodoroState.Paused);
            pomo.Resume();
            pomo.State.Should().Be(PomodoroState.Focus);
            pomo.Stop(true); // 清理
        }

        [Fact]
        public void Stop_IdleState_ShouldDoNothing()
        {
            var pomo = PomodoroService.Instance;
            pomo.Stop(true);
            pomo.State.Should().Be(PomodoroState.Idle);
        }

        [Fact]
        public void CompleteSession_ShouldRecordAndRewardXp()
        {
            var pomo = PomodoroService.Instance;
            var beforeXp = LevelService.Instance.GetGrowth().TotalXp;
            var beforeStats = pomo.GetTodayStats();

            pomo.Start(null, "", 1);
            pomo.Stop(true);

            var afterStats = pomo.GetTodayStats();
            afterStats.completed.Should().Be(beforeStats.completed + 1);
            LevelService.Instance.GetGrowth().TotalXp.Should().Be(beforeXp + 5); // 未绑定任务 +5
        }

        [Fact]
        public void InterruptedSession_ShouldNotRewardXp()
        {
            var pomo = PomodoroService.Instance;
            var beforeXp = LevelService.Instance.GetGrowth().TotalXp;
            var beforeStats = pomo.GetTodayStats();

            pomo.Start(null, "", 1);
            pomo.Stop(false); // 中断

            var afterStats = pomo.GetTodayStats();
            afterStats.interrupted.Should().Be(beforeStats.interrupted + 1);
            afterStats.completed.Should().Be(beforeStats.completed);
            LevelService.Instance.GetGrowth().TotalXp.Should().Be(beforeXp); // 中断不计 XP
        }
    }
}
