using System;
using TodoSidebar.ViewModels;
using Xunit;

namespace TodoSidebar.Tests
{
    /// <summary>v5.3 热力图单元格：色阶映射与展示逻辑。</summary>
    public class HeatmapDayTests
    {
        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 1)]
        [InlineData(2, 1)]
        [InlineData(3, 2)]
        [InlineData(5, 2)]
        [InlineData(6, 3)]
        [InlineData(9, 3)]
        [InlineData(10, 4)]
        [InlineData(50, 4)]
        public void Level_CountThresholds_MappedCorrectly(int count, int expectedLevel)
        {
            var day = new HeatmapDay(new DateTime(2026, 8, 25), count);
            Assert.Equal(expectedLevel, day.Level);
        }

        [Fact]
        public void Blank_HasNoDateAndNoToolTip()
        {
            Assert.True(HeatmapDay.Blank.IsBlank);
            Assert.False(HeatmapDay.Blank.Date.HasValue);
            Assert.Equal(string.Empty, HeatmapDay.Blank.ToolTip);
        }

        [Fact]
        public void ToolTip_ContainsDateAndCount()
        {
            var day = new HeatmapDay(new DateTime(2026, 8, 25), 7);
            var tip = day.ToolTip;
            Assert.Contains("8月25日", tip);
            Assert.Contains("7", tip);
        }
    }
}
