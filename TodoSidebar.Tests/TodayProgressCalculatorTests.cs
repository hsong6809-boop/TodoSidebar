using TodoSidebar.Services;
using Xunit;

namespace TodoSidebar.Tests
{
    /// <summary>B3：今日进度共享公式——分母含今日已完成截止任务；比率 clamp。</summary>
    public class TodayProgressCalculatorTests
    {
        [Fact]
        public void EmptyPlan_RateIsZero()
        {
            var r = TodayProgressCalculator.Calculate(0, 0, 0);
            Assert.Equal(0, r.Done);
            Assert.Equal(0, r.Planned);
            Assert.Equal(0, r.Rate);
            Assert.False(r.IsOverachieving);
        }

        [Fact]
        public void Denominator_IncludesCompletedDueToday()
        {
            // 3 每日 + 2 今日到期（1 完成 + 1 未完成）→ M=5
            // 完成：1 每日 + 1 到期 = 2
            var r = TodayProgressCalculator.Calculate(
                completedToday: 2,
                dailyTaskCount: 3,
                dueTodayCount: 2);
            Assert.Equal(5, r.Planned);
            Assert.Equal(2, r.Done);
            Assert.Equal(0.4, r.Rate, 10);
            Assert.False(r.IsOverachieving);
        }

        [Fact]
        public void Overachieving_RateClampedToOne()
        {
            // 补做逾期使 N > M
            var r = TodayProgressCalculator.Calculate(
                completedToday: 6,
                dailyTaskCount: 2,
                dueTodayCount: 2);
            Assert.Equal(4, r.Planned);
            Assert.True(r.IsOverachieving);
            Assert.Equal(1.0, r.Rate);
            Assert.Contains("✨", r.DoneText);
        }

        [Fact]
        public void FullClear_RateIsOne()
        {
            var r = TodayProgressCalculator.Calculate(3, 2, 1);
            Assert.Equal(3, r.Planned);
            Assert.Equal(1.0, r.Rate);
            Assert.False(r.IsOverachieving);
        }

        [Fact]
        public void NegativeInputs_ClampToZero()
        {
            var r = TodayProgressCalculator.Calculate(-1, -2, -3);
            Assert.Equal(0, r.Done);
            Assert.Equal(0, r.Planned);
            Assert.Equal(0, r.Rate);
        }
    }
}
