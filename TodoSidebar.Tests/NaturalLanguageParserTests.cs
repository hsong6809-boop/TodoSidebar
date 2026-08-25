using System;
using System.Linq;
using TodoSidebar.Models;
using TodoSidebar.Services;
using Xunit;

namespace TodoSidebar.Tests
{
    /// <summary>V5.1：自然语言快速录入解析器测试。</summary>
    public class NaturalLanguageParserTests
    {
        [Fact]
        public void Parse_PlainTitle_KeepsTextAndDefaults()
        {
            var p = NaturalLanguageParser.Parse("买牛奶");
            Assert.Equal("买牛奶", p.Title);
            Assert.False(p.HasDue);
            Assert.Null(p.Priority);
            Assert.Empty(p.Tags);
        }

        [Fact]
        public void Parse_TomorrowAfternoon3_ParsesDueAndPriority()
        {
            var p = NaturalLanguageParser.Parse("明天下午3点 交周报 #工作 紧急");
            Assert.Equal("交周报", p.Title);
            Assert.True(p.HasDue);
            Assert.Equal(DateTime.Today.AddDays(1).Date, p.DueDate!.Value.Date);
            Assert.Equal(15, p.DueDate.Value.Hour);
            Assert.Equal(TaskPriority.High, p.Priority);
            Assert.Contains("工作", p.Tags);
        }

        [Fact]
        public void Parse_Weekday_ResolvesUpcomingOccurrence()
        {
            // 固定“今天”的星期，验证 delta 计算：任选未来最近的出现日
            var p = NaturalLanguageParser.Parse("周五 提交代码 #dev");
            Assert.Equal("提交代码", p.Title);
            Assert.True(p.HasDue);
            Assert.Equal(DayOfWeek.Friday, p.DueDate!.Value.DayOfWeek);
            Assert.Contains("dev", p.Tags);
        }

        [Fact]
        public void Parse_MonthDayWithTime_ParsesExplicitDate()
        {
            var p = NaturalLanguageParser.Parse("9月30日 10:20 发布新版本");
            Assert.Equal("发布新版本", p.Title);
            Assert.True(p.HasDue);
            var due = p.DueDate!.Value;
            Assert.Equal(9, due.Month);
            Assert.Equal(30, due.Day);
            Assert.Equal(10, due.Hour);
            Assert.Equal(20, due.Minute);
        }

        [Fact]
        public void Parse_RelativeHours_ShiftsFromNow()
        {
            var before = DateTime.Now.AddHours(2).AddMinutes(-2);
            var after = DateTime.Now.AddHours(2).AddMinutes(2);
            var p = NaturalLanguageParser.Parse("2小时后 开会");
            Assert.Equal("开会", p.Title);
            Assert.True(p.HasDue);
            Assert.InRange(p.DueDate!.Value, before, after);
        }

        [Fact]
        public void Parse_HalfHourLater_Works()
        {
            var p = NaturalLanguageParser.Parse("半小时后 站起来活动");
            Assert.Equal("站起来活动", p.Title);
            Assert.True(p.HasDue);
            var delta = p.DueDate!.Value - DateTime.Now;
            Assert.InRange(delta.TotalMinutes, 25, 35);
        }

        [Fact]
        public void Parse_MultipleTags_AreAllCollected()
        {
            var p = NaturalLanguageParser.Parse("#工作 #紧急项目 覆盖文档 #生活");
            Assert.Equal("覆盖文档", p.Title);
            Assert.Equal(3, p.Tags.Count);
        }

        [Fact]
        public void Parse_LowPriority_Token()
        {
            var p = NaturalLanguageParser.Parse("整理桌面 低优");
            Assert.Equal("整理桌面", p.Title);
            Assert.Equal(TaskPriority.Low, p.Priority);
        }

        [Fact]
        public void Parse_NextWeekWednesday_ShiftsToNextNaturalWeek()
        {
            // “下周三”按下一自然周（周一起点）计算，与“周三”（本周最近一次）区分
            var p = NaturalLanguageParser.Parse("下周三 9点 周报");
            Assert.Equal("周报", p.Title);
            Assert.True(p.HasDue);
            var due = p.DueDate!.Value;
            Assert.Equal(DayOfWeek.Wednesday, due.DayOfWeek);
            Assert.Equal(9, due.Hour);
            Assert.Equal(0, due.Minute);

            var todayDow = (int)DateTime.Today.DayOfWeek; // Sun=0 … Sat=6
            var daysToNextMonday = (8 - todayDow) % 7;
            if (daysToNextMonday == 0) daysToNextMonday = 7;
            var nextMonday = DateTime.Today.AddDays(daysToNextMonday);
            Assert.InRange(due.Date, nextMonday, nextMonday.AddDays(6));
        }

        [Fact]
        public void Parse_NextWeekSunday_LandsAtWeekEnd()
        {
            var p = NaturalLanguageParser.Parse("下周日 交总结");
            Assert.True(p.HasDue);
            Assert.Equal(DayOfWeek.Sunday, p.DueDate!.Value.DayOfWeek);

            var todayDow = (int)DateTime.Today.DayOfWeek;
            var daysToNextMonday = (8 - todayDow) % 7;
            if (daysToNextMonday == 0) daysToNextMonday = 7;
            var nextMonday = DateTime.Today.AddDays(daysToNextMonday);
            Assert.Equal(nextMonday.AddDays(6), p.DueDate!.Value.Date);
        }

        [Fact]
        public void Parse_BareTime_ResolvesTo15_00TodayOrTomorrow()
        {
            var p = NaturalLanguageParser.Parse("下午3点 开会");
            Assert.Equal("开会", p.Title);
            Assert.True(p.HasDue);
            var due = p.DueDate!.Value;
            Assert.Equal(15, due.Hour);
            Assert.Equal(0, due.Minute);
            // 已过则顺延到明天
            Assert.True(due.Date == DateTime.Today || due.Date == DateTime.Today.AddDays(1));
        }

        [Fact]
        public void Parse_DateOnly_NoKeyword_KeepsNoPriority()
        {
            var p = NaturalLanguageParser.Parse("明天 交周报");
            Assert.Equal("交周报", p.Title);
            Assert.True(p.HasDue);
            Assert.Equal(DateTime.Today.AddDays(1).Date, p.DueDate!.Value.Date);
            Assert.Equal(0, p.DueDate!.Value.Hour); // 未写时间 → 当天 0 点
            Assert.Null(p.Priority);
        }
    }
}
