using FluentAssertions;
using Xunit;
using TodoSidebar.Services;

namespace TodoSidebar.Tests
{
    public class LicenseServiceTests
    {
        [Fact]
        public void NewService_ShouldBeFree()
        {
            var service = new LicenseService();
            service.CurrentTier.Should().Be(LicenseTier.Free);
            service.IsPro.Should().BeFalse();
        }

        [Fact]
        public void ActivateLicense_ShouldReturnFalse()
        {
            var service = new LicenseService();
            var result = service.ActivateLicense("any-key");
            result.Should().BeFalse();
            service.CurrentTier.Should().Be(LicenseTier.Free);
        }

        [Fact]
        public void ValidateLicense_FreeTier_ShouldReturnTrue()
        {
            var service = new LicenseService();
            service.ValidateLicense().Should().BeTrue();
        }

        [Fact]
        public void Trial_NotStarted_ShouldBeInactive()
        {
            var service = new LicenseService();
            service.IsTrialActive.Should().BeFalse();
            service.TrialDaysRemaining.Should().Be(0);
        }

        [Fact]
        public void StartTrial_ShouldActivateTrial()
        {
            var service = new LicenseService();
            service.StartTrial();
            service.IsTrialActive.Should().BeTrue();
            service.TrialDaysRemaining.Should().Be(14);
        }

        [Fact]
        public void StartTrial_CalledTwice_ShouldNotRestart()
        {
            var service = new LicenseService();
            service.StartTrial();
            var remaining1 = service.TrialDaysRemaining;
            
            // 第二次调用不应重置计时
            service.StartTrial();
            var remaining2 = service.TrialDaysRemaining;
            remaining1.Should().Be(remaining2);
        }

        [Fact]
        public void ClearLicense_ShouldResetToFree()
        {
            var service = new LicenseService();
            service.StartTrial();
            service.ClearLicense();
            
            service.CurrentTier.Should().Be(LicenseTier.Free);
            service.IsTrialActive.Should().BeFalse();
            service.TrialDaysRemaining.Should().Be(0);
        }

        [Fact]
        public void ClearLicense_WhenAlreadyFree_ShouldNotFireTierChanged()
        {
            var service = new LicenseService();
            var eventFired = false;
            service.TierChanged += (_, tier) => eventFired = true;
            
            // 已经是 Free，ClearLicense 不应触发事件
            service.ClearLicense();
            eventFired.Should().BeFalse();
        }

        [Fact]
        public void ProExpiryDate_ShouldBeNull()
        {
            var service = new LicenseService();
            service.ProExpiryDate.Should().BeNull();
        }
    }
}
