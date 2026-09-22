using System;
using TodoSidebar.Models;
using TodoSidebar.Services;
using Xunit;

namespace TodoSidebar.Tests
{
    /// <summary>B9：NLP 中文数字「十一/十二个半小时」等边界；「后」可选。</summary>
    public class NlpChineseHalfHoursTests
    {
        [Theory]
        [InlineData("十一个半小时后 取快递", 11.5)]
        [InlineData("十二个半小时后 开会", 12.5)]
        [InlineData("二十个半小时后 出发", 20.5)]
        [InlineData("十一个半小时 取快递", 11.5)]
        [InlineData("两个半小时 开会", 2.5)]
        public void Parse_ChineseHalfHours_OptionalHou(string raw, double expectedHours)
        {
            var before = DateTime.Now.AddHours(expectedHours).AddMinutes(-2);
            var after = DateTime.Now.AddHours(expectedHours).AddMinutes(2);
            var p = NaturalLanguageParser.Parse(raw);
            Assert.True(p.HasDue, raw);
            Assert.InRange(p.DueDate!.Value, before, after);
        }

        [Theory]
        [InlineData("十一", 11)]
        [InlineData("十二", 12)]
        [InlineData("二十", 20)]
        [InlineData("二十一", 21)]
        [InlineData("两", 2)]
        [InlineData("十", 10)]
        public void ParseChineseNumber_HandlesTwoDigit(string s, int expected)
        {
            Assert.Equal(expected, NaturalLanguageParser.ParseChineseNumber(s));
        }
    }
}
