using System;
using TodoSidebar.Models;
using Xunit;

namespace TodoSidebar.Tests
{
    /// <summary>v5.4 重复任务规则引擎测试。</summary>
    public class RecurrenceRuleTests
    {
        // ========== 合法性 / 规范化 ==========

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("daily")]
        [InlineData("weekdays")]
        [InlineData("monthly")]
        [InlineData("weekly:1")]
        [InlineData("weekly:7")]
        public void IsValid_ValidRules_True(string? rule)
            => Assert.True(RecurrenceRule.IsValid(rule));

        [Theory]
        [InlineData("weekly:0")]    // 越界
        [InlineData("weekly:8")]
        [InlineData("weekly:12")]
        [InlineData("weekly")]
        [InlineData("yearly")]
        [InlineData("DAILY_X")]
        public void IsValid_InvalidRules_False(string? rule)
            => Assert.False(RecurrenceRule.IsValid(rule));

        [Fact]
        public void Normalize_CaseAndWhitespace_Canonicalized()
        {
            Assert.Equal("daily", RecurrenceRule.Normalize(" Daily "));
            Assert.Equal("weekly:3", RecurrenceRule.Normalize("WEEKLY:3"));
            Assert.Null(RecurrenceRule.Normalize("  "));
            Assert.Null(RecurrenceRule.Normalize("bogus"));
        }

        // ========== 下一期计算 ==========
        // 基准日：2026-08-26 为周三

        private static readonly DateTime Wed = new(2026, 8, 26);

        [Fact]
        public void NextDeadline_Daily_NextDay()
        {
            Assert.Equal(new DateTime(2026, 8, 27), RecurrenceRule.NextDeadline("daily", Wed));
        }

        [Fact]
        public void NextDeadline_Weekdays_SkipsWeekend()
        {
            // 周五 8/28 完成后 → 下一个是周一 8/31
            var friday = new DateTime(2026, 8, 28);
            Assert.Equal(new DateTime(2026, 8, 31), RecurrenceRule.NextDeadline("weekdays", friday));
        }

        [Fact]
        public void NextDeadline_WeeklyN_NextOccurrenceStrictlyAfter()
        {
            // 周三完成"每周一"→ 下一个周一是 8/31（严格晚于基准）
            Assert.Equal(new DateTime(2026, 8, 31), RecurrenceRule.NextDeadline("weekly:1", Wed));
            // 周三完成"每周三"→ 不允许同日，顺延到下周三 9/2
            Assert.Equal(new DateTime(2026, 9, 2), RecurrenceRule.NextDeadline("weekly:3", Wed));
            // 编码 7=周日：周日 8/30 完成 → 下个周日 9/6
            var sunday = new DateTime(2026, 8, 30);
            Assert.Equal(new DateTime(2026, 9, 6), RecurrenceRule.NextDeadline("weekly:7", sunday));
        }

        [Fact]
        public void NextDeadline_Monthly_ClampsToMonthEnd()
        {
            var beforeJan = new DateTime(2026, 1, 10);
            // 1/31 → 2/28（2026 非闰年）
            var jan31 = new DateTime(2026, 1, 31);
            Assert.Equal(new DateTime(2026, 2, 28),
                RecurrenceRule.NextDeadline("monthly", jan31, today: beforeJan));
            // 1/15 → 2/15 正常
            Assert.Equal(new DateTime(2026, 2, 15),
                RecurrenceRule.NextDeadline("monthly", new DateTime(2026, 1, 15), today: beforeJan));
        }

        [Fact]
        public void NextDeadline_OverdueBase_ClampedToToday()
        {
            // 基准已逾期一周：下一期从"今天之后"起算，避免连锁生成过期实例
            var stale = new DateTime(2026, 8, 10);
            var next = RecurrenceRule.NextDeadline("daily", stale, today: Wed);
            Assert.Equal(new DateTime(2026, 8, 27), next);
        }

        [Fact]
        public void NextDeadline_EmptyOrNull_ReturnsNull()
        {
            Assert.Null(RecurrenceRule.NextDeadline(null, Wed));
            Assert.Null(RecurrenceRule.NextDeadline("", Wed));
            Assert.Null(RecurrenceRule.NextDeadline("bogus", Wed));
        }

        [Fact]
        public void LabelOf_KnownRules_ReturnChineseLabels()
        {
            Assert.Equal("不重复", RecurrenceRule.LabelOf(null));
            Assert.Equal("每天", RecurrenceRule.LabelOf("daily"));
            Assert.Equal("每周三", RecurrenceRule.LabelOf("weekly:3"));
            Assert.Equal("每月同一天", RecurrenceRule.LabelOf("monthly"));
        }
    }
}
