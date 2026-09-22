using System;
using System.Collections.Generic;
using TodoSidebar.Models;
using Xunit;

namespace TodoSidebar.Tests
{
    /// <summary>B4：循环派生幂等 + 取消完成收回派生（纯函数，不碰 DB）。</summary>
    public class RecurrenceSpawnLogicTests
    {
        private static readonly DateTime Base = new(2026, 9, 22);
        private static readonly DateTime Today = new(2026, 9, 22);

        [Fact]
        public void HasLiveSpawnFor_WhenNextExists_ReturnsTrue()
        {
            var next = RecurrenceRule.NextDeadline(RecurrenceRule.Daily, Base, Today)!.Value;
            var existing = new List<(string, string?, DateTime?, bool)>
            {
                ("写周报", RecurrenceRule.Daily, next, false),
            };
            Assert.True(RecurrenceRule.HasLiveSpawnFor("写周报", RecurrenceRule.Daily, next, existing));
        }

        [Fact]
        public void HasLiveSpawnFor_IgnoresDeletedAndOtherSeries()
        {
            var next = RecurrenceRule.NextDeadline(RecurrenceRule.Daily, Base, Today)!.Value;
            var existing = new List<(string, string?, DateTime?, bool)>
            {
                ("写周报", RecurrenceRule.Daily, next, isDeleted: true),   // 已删
                ("其他", RecurrenceRule.Daily, next, false),               // 不同标题
                ("写周报", RecurrenceRule.WeeklyPrefix + "1", next, false), // 不同规则
            };
            Assert.False(RecurrenceRule.HasLiveSpawnFor("写周报", RecurrenceRule.Daily, next, existing));
        }

        [Fact]
        public void FindSpawnsToRetract_ReturnsUncompletedNextOnly()
        {
            var next = RecurrenceRule.NextDeadline(RecurrenceRule.Daily, Base, Today)!.Value;
            var candidates = new List<(int, string, string?, DateTime?, bool, bool)>
            {
                (11, "写周报", RecurrenceRule.Daily, next, false, false),          // 应收回
                (12, "写周报", RecurrenceRule.Daily, next, false, true),           // 已完成 → 不收回
                (13, "写周报", RecurrenceRule.Daily, next, true, false),           // 已删 → 不收回
                (14, "写周报", RecurrenceRule.Daily, next.AddDays(1), false, false), // 非 next
                (15, "其他", RecurrenceRule.Daily, next, false, false),
            };
            var ids = RecurrenceRule.FindSpawnsToRetract(
                "写周报", RecurrenceRule.Daily, Base, candidates, Today);
            Assert.Equal(new List<int> { 11 }, ids);
        }

        [Fact]
        public void FindSpawnsToRetract_NoRule_ReturnsEmpty()
        {
            var candidates = new List<(int, string, string?, DateTime?, bool, bool)>();
            Assert.Empty(RecurrenceRule.FindSpawnsToRetract("x", null, Base, candidates, Today));
        }

        [Fact]
        public void SpawnAnchor_NextDeadlineIsIdempotentKey()
        {
            // 完成→取消→再完成：两次算出的 next 相同 → HasLiveSpawn 挡住幽灵
            var n1 = RecurrenceRule.NextDeadline(RecurrenceRule.Daily, Base, Today);
            var n2 = RecurrenceRule.NextDeadline(RecurrenceRule.Daily, Base, Today);
            Assert.Equal(n1, n2);
        }
    }
}
