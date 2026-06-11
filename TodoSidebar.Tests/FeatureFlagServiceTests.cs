using FluentAssertions;
using Xunit;
using TodoSidebar.Services;

namespace TodoSidebar.Tests
{
    public class FeatureFlagServiceTests
    {
        private FeatureFlagService CreateService(bool isPro = false)
        {
            var license = new LicenseService();
            // LicenseService always returns IsPro=false, so we test the Free path
            return new FeatureFlagService(license);
        }

        [Fact]
        public void FreeUser_ProFeatures_ShouldBeEnabled()
        {
            var service = CreateService();
            // v4.0.0 策略：商业化骨架就位，暂不限制任何功能
            service.CloudSync.Should().BeTrue();
            service.AdvancedSearch.Should().BeTrue();
            service.TagSystem.Should().BeTrue();
            service.Statistics.Should().BeTrue();
        }

        [Fact]
        public void IsProFeature_KnownProFeatures_ShouldReturnTrue()
        {
            var service = CreateService();
            service.IsProFeature("CloudSync").Should().BeTrue();
            service.IsProFeature("AdvancedSearch").Should().BeTrue();
            service.IsProFeature("Statistics").Should().BeTrue();
        }

        [Fact]
        public void IsProFeature_UnknownFeature_ShouldReturnFalse()
        {
            var service = CreateService();
            service.IsProFeature("BasicTaskManagement").Should().BeFalse();
            service.IsProFeature("SidebarMode").Should().BeFalse();
        }

        [Fact]
        public void IsEnabled_UnknownFeature_ShouldReturnTrue()
        {
            var service = CreateService();
            // 非 Pro 功能始终启用
            service.IsEnabled("BasicTaskManagement").Should().BeTrue();
            service.IsEnabled("SidebarMode").Should().BeTrue();
        }

        [Fact]
        public void SetFlag_Override_ShouldAffectIsEnabled()
        {
            var service = CreateService();
            
            // 手动启用 CloudSync（即使 Free 用户）
            service.SetFlag("CloudSync", true);
            service.IsEnabled("CloudSync").Should().BeTrue();
            service.CloudSync.Should().BeTrue();
        }

        [Fact]
        public void SetFlag_OverrideDisable_ShouldAffectIsEnabled()
        {
            var service = CreateService();
            
            // 手动禁用普通功能
            service.SetFlag("BasicTaskManagement", false);
            service.IsEnabled("BasicTaskManagement").Should().BeFalse();
        }

        [Fact]
        public void SetFlag_ClearOverride_ShouldRevertToDefault()
        {
            var service = CreateService();
            
            service.SetFlag("CloudSync", true);
            service.CloudSync.Should().BeTrue();
            
            service.SetFlag("CloudSync", false);
            service.CloudSync.Should().BeFalse();
        }
    }
}
