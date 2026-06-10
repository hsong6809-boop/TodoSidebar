using FluentAssertions;
using Xunit;
using TodoSidebar.Services;

namespace TodoSidebar.Tests
{
    public class DataProtectionHelperTests
    {
        [Fact]
        public void Protect_ShouldReturnNonEmptyString()
        {
            var result = DataProtectionHelper.Protect("test data");
            result.Should().NotBeNullOrEmpty();
            result.Should().NotBe("test data"); // 应该是加密后的
        }

        [Fact]
        public void Unprotect_ShouldRestoreOriginalText()
        {
            var original = "my secret session token";
            var encrypted = DataProtectionHelper.Protect(original);
            var decrypted = DataProtectionHelper.Unprotect(encrypted);
            decrypted.Should().Be(original);
        }

        [Fact]
        public void Protect_EmptyString_ShouldReturnEmpty()
        {
            DataProtectionHelper.Protect("").Should().Be("");
            DataProtectionHelper.Protect(null!).Should().Be("");
        }

        [Fact]
        public void Unprotect_EmptyString_ShouldReturnEmpty()
        {
            DataProtectionHelper.Unprotect("").Should().Be("");
            DataProtectionHelper.Unprotect(null!).Should().Be("");
        }

        [Fact]
        public void Unprotect_PlainText_ShouldReturnAsIs()
        {
            // 兼容旧版明文格式
            var plainText = "old format session data";
            var result = DataProtectionHelper.Unprotect(plainText);
            result.Should().Be(plainText);
        }

        [Fact]
        public void IsProtected_EncryptedData_ShouldReturnTrue()
        {
            var encrypted = DataProtectionHelper.Protect("test");
            DataProtectionHelper.IsProtected(encrypted).Should().BeTrue();
        }

        [Fact]
        public void IsProtected_ShortString_ShouldReturnFalse()
        {
            DataProtectionHelper.IsProtected("abc").Should().BeFalse();
            DataProtectionHelper.IsProtected("").Should().BeFalse();
        }

        [Fact]
        public void RoundTrip_Unicode_ShouldWork()
        {
            var original = "中文测试数据 🔑 token_abc123";
            var encrypted = DataProtectionHelper.Protect(original);
            var decrypted = DataProtectionHelper.Unprotect(encrypted);
            decrypted.Should().Be(original);
        }

        [Fact]
        public void RoundTrip_LongString_ShouldWork()
        {
            var original = new string('x', 10000);
            var encrypted = DataProtectionHelper.Protect(original);
            var decrypted = DataProtectionHelper.Unprotect(encrypted);
            decrypted.Should().Be(original);
        }
    }
}
