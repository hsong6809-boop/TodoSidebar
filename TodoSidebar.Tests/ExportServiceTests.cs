using System;
using TodoSidebar.Models;
using TodoSidebar.Services;
using Xunit;

namespace TodoSidebar.Tests
{
    /// <summary>
    /// R71：ExportService 纯逻辑测试（Markdown 导出片段）。
    /// 只覆盖不依赖 DatabaseService / 文件系统的 static 函数
    /// （EscapeMarkdown / FormatTaskSuffix 已改为 internal static，见 TodoSidebar.csproj 的 InternalsVisibleTo）；
    /// 导出/导入/备份等需要真实 SQLite 与磁盘的路径不在本文件测。
    /// </summary>
    public class ExportServiceTests
    {
        // ========== EscapeMarkdown：反斜杠 ==========

        [Fact]
        public void EscapeMarkdown_PlainText_Unchanged()
            => Assert.Equal("买牛奶", ExportService.EscapeMarkdown("买牛奶"));

        [Fact]
        public void EscapeMarkdown_Backslash_EscapedFirst()
            => Assert.Equal("a" + new string('\\', 2) + "b", ExportService.EscapeMarkdown("a\\b"));

        [Fact]
        public void EscapeMarkdown_BackslashFollowedByStar_InsertedBackslashNotEscapedTwice()
        {
            // 顺序回归：'\' 必须最先转义。否则第 5 步 "*" → "\*" 插入的反斜杠会被再次转义，
            // 结果从 3 个反斜杠变成 4 个（即 "\\\\*"，Markdown 中会退化成字面 "\" + 斜体）
            var actual = ExportService.EscapeMarkdown("a\\*b");
            Assert.Equal("a" + new string('\\', 3) + "*b", actual);
        }

        // ========== EscapeMarkdown：Markdown 元字符 ==========

        [Fact]
        public void EscapeMarkdown_Asterisk_Escaped()
            => Assert.Equal("2\\*3\\*4", ExportService.EscapeMarkdown("2*3*4"));

        [Fact]
        public void EscapeMarkdown_EmphasisMarkers_Escaped()
            => Assert.Equal("\\*\\*重点\\*\\*", ExportService.EscapeMarkdown("**重点**"));

        [Fact]
        public void EscapeMarkdown_Underscore_Escaped()
            => Assert.Equal("snake\\_case", ExportService.EscapeMarkdown("snake_case"));

        [Fact]
        public void EscapeMarkdown_SquareBrackets_Escaped()
            => Assert.Equal("\\[链接\\]", ExportService.EscapeMarkdown("[链接]"));

        [Fact]
        public void EscapeMarkdown_Backtick_Escaped()
            => Assert.Equal("\\`code\\`", ExportService.EscapeMarkdown("`code`"));

        [Fact]
        public void EscapeMarkdown_MixedMetacharacters_AllEscaped()
        {
            // 综合用例：反引号 + 下划线 + 星号 + 方括号
            Assert.Equal("a\\`b\\_c\\*d\\[e\\]",
                ExportService.EscapeMarkdown("a`b_c*d[e]"));
        }

        // ========== EscapeMarkdown：换行 ==========

        [Theory]
        [InlineData("a\r\nb", "a  b")]   // CRLF → 两个空格（逐字符替换）
        [InlineData("a\nb", "a b")]
        [InlineData("a\rb", "a b")]
        [InlineData("\n\n", "  ")]
        public void EscapeMarkdown_Newlines_ReplacedWithSpace(string input, string expected)
            => Assert.Equal(expected, ExportService.EscapeMarkdown(input));

        // ========== FormatTaskSuffix：标签（逗号分隔 → #标签） ==========

        [Fact]
        public void FormatTaskSuffix_TwoTags_SplitByCommaNotHash()
        {
            // R71 核心回归：存库是 "工作,生活"（MainViewModel.ApplyPendingTags 写 string.Join(",", tags)），
            // 原实现按 '#' 分割 → 输出 "（#工作,生活）"
            var suffix = ExportService.FormatTaskSuffix(
                new TaskItem { Title = "交周报", Type = TaskType.Daily, Tags = "工作,生活" });

            Assert.Equal(" （#工作 #生活）", suffix);
            Assert.DoesNotContain("#工作,生活", suffix);
        }

        [Fact]
        public void FormatTaskSuffix_SingleTag_OneHash()
            => Assert.Equal(" （#工作）", ExportService.FormatTaskSuffix(
                new TaskItem { Type = TaskType.Daily, Tags = "工作" }));

        [Fact]
        public void FormatTaskSuffix_TagsWithSpaces_Trimmed()
            => Assert.Equal(" （#工作 #生活）", ExportService.FormatTaskSuffix(
                new TaskItem { Type = TaskType.Daily, Tags = " 工作 , 生活 " }));

        [Fact]
        public void FormatTaskSuffix_AlreadyPrefixedTags_NoDoubleHash()
            => Assert.Equal(" （#工作 #生活）", ExportService.FormatTaskSuffix(
                new TaskItem { Type = TaskType.Daily, Tags = "#工作,#生活" }));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(",")]
        [InlineData(",,,")]
        public void FormatTaskSuffix_EmptyOrSeparatorOnlyTags_NoTagSuffix(string? tags)
            => Assert.Equal("", ExportService.FormatTaskSuffix(
                new TaskItem { Type = TaskType.Daily, Tags = tags }));

        [Fact]
        public void FormatTaskSuffix_NoTagsNoDeadline_Empty()
            => Assert.Equal("", ExportService.FormatTaskSuffix(
                new TaskItem { Type = TaskType.Daily }));

        [Fact]
        public void FormatTaskSuffix_DeadlineTask_IncludesDeadlineAndTags()
        {
            var suffix = ExportService.FormatTaskSuffix(new TaskItem
            {
                Type = TaskType.Deadline,
                Deadline = new DateTime(2024, 8, 31, 10, 30, 0),
                Tags = "工作,生活"
            });

            Assert.Equal(" （截止 08-31 10:30 · #工作 #生活）", suffix);
        }

        [Fact]
        public void FormatTaskSuffix_DailyTaskWithDeadlineValue_DoesNotEmitDeadlinePart()
        {
            // 每日任务即使带了 Deadline 值也不输出截止段（与导出模板原有判断一致）
            var suffix = ExportService.FormatTaskSuffix(new TaskItem
            {
                Type = TaskType.Daily,
                Deadline = new DateTime(2024, 8, 31, 10, 30, 0),
                Tags = "工作"
            });

            Assert.Equal(" （#工作）", suffix);
        }
    }
}
