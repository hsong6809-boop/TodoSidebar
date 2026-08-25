using System.Linq;
using TodoSidebar.Services;
using Xunit;

namespace TodoSidebar.Tests
{
    /// <summary>v5.2 账号中心：AccountService 纯逻辑测试。</summary>
    public class AccountServiceTests
    {
        // ========== 短 ID ==========

        [Theory]
        [InlineData("48213905")]
        [InlineData("10000000")]
        [InlineData("99999999")]
        public void IsValidUid_EightDigitsNonZeroStart_True(string uid)
            => Assert.True(AccountService.IsValidUid(uid));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("04821390")]   // 首位为零
        [InlineData("4821390")]    // 7 位
        [InlineData("482139056")]  // 9 位
        [InlineData("482139o5")]   // 非数字
        public void IsValidUid_InvalidInput_False(string? uid)
            => Assert.False(AccountService.IsValidUid(uid));

        [Fact]
        public void GenerateUid_AlwaysValidFormat()
        {
            for (int i = 0; i < 200; i++)
                Assert.True(AccountService.IsValidUid(AccountService.GenerateUid()),
                    $"第 {i} 次生成的 UID 格式非法");
        }

        // ========== 头像类型规范化 ==========

        [Theory]
        [InlineData("d1", "d1")]
        [InlineData("d8", "d8")]
        [InlineData("D3", "d3")]      // 大小写兼容
        [InlineData(" d5 ", "d5")]    // 容忍空白
        [InlineData("custom", "custom")]
        [InlineData("CUSTOM", "custom")]
        public void NormalizeKind_ValidInputs_Canonicalized(string input, string expected)
            => Assert.Equal(expected, AccountService.NormalizeKind(input));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("d0")]     // 越界
        [InlineData("d9")]     // 超出内置数
        [InlineData("x3")]     // 前缀错误
        [InlineData("vector")] // 未知类型
        public void NormalizeKind_InvalidInputs_FallbackD1(string? input)
            => Assert.Equal("d1", AccountService.NormalizeKind(input));

        [Fact]
        public void BuiltInAvatarCount_Is8()
            => Assert.Equal(8, AccountService.BuiltInAvatarCount);

        // ========== 昵称清洗 ==========

        [Fact]
        public void CleanNickname_TrimsAndCollapsesWhitespace()
            => Assert.Equal("冒险家 Alpha", AccountService.CleanNickname("  冒险家\u3000 Alpha\t "));

        [Fact]
        public void CleanNickname_StripsControlCharsAndNewlines()
            => Assert.Equal("ab", AccountService.CleanNickname("a\nb\r"));

        [Fact]
        public void CleanNickname_NullOrWhitespace_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, AccountService.CleanNickname(null));
            Assert.Equal(string.Empty, AccountService.CleanNickname("   "));
        }

        [Fact]
        public void CleanNickname_OverLong_TruncatedTo24()
        {
            var longName = new string('名', 40);
            var cleaned = AccountService.CleanNickname(longName);
            Assert.Equal(AccountService.NicknameMaxLength, cleaned.Length);
        }

        [Fact]
        public void CleanNickname_KeepsHashTagAndSymbols()
            => Assert.Equal("#工作_1号", AccountService.CleanNickname("#工作_1号"));
    }
}
