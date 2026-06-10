using System.Collections.Generic;

namespace TodoSidebar.Services
{
    /// <summary>
    /// Feature Flag 服务 — 基于 ILicenseService 动态判断。
    /// Pro 功能在 Free 模式下全部禁用。
    /// </summary>
    public class FeatureFlagService : IFeatureFlagService
    {
        private readonly ILicenseService _licenseService;

        /// <summary>手动覆盖的 flag（用于测试或特殊场景）</summary>
        private readonly Dictionary<string, bool> _overrides = new();

        /// <summary>Pro 专属功能列表</summary>
        private static readonly HashSet<string> ProFeatures = new()
        {
            "CloudSync",
            "AdvancedSearch",
            "TagSystem",
            "Statistics",
            "CustomThemes",
            "UnlimitedTemplates",
            "DataExportCsv",
            "DataExportMarkdown"
        };

        public FeatureFlagService(ILicenseService licenseService)
        {
            _licenseService = licenseService;
        }

        public bool IsEnabled(string featureKey)
        {
            // 手动覆盖优先
            if (_overrides.TryGetValue(featureKey, out var overrideValue))
                return overrideValue;

            // 当前阶段：所有功能对所有用户开放（不根据 License 限制）
            // 等商业化验证通过后再启用 License 检查
            return true;
        }

        public bool IsProFeature(string featureKey)
        {
            return ProFeatures.Contains(featureKey);
        }

        public void SetFlag(string featureKey, bool enabled)
        {
            _overrides[featureKey] = enabled;
        }

        // === 预定义属性（委托给 IsEnabled）===

        public bool CloudSync => IsEnabled("CloudSync");
        public bool AdvancedSearch => IsEnabled("AdvancedSearch");
        public bool TagSystem => IsEnabled("TagSystem");
        public bool Statistics => IsEnabled("Statistics");
        public bool CustomThemes => IsEnabled("CustomThemes");
        public bool UnlimitedTemplates => IsEnabled("UnlimitedTemplates");
        public bool DataExportCsv => IsEnabled("DataExportCsv");
        public bool DataExportMarkdown => IsEnabled("DataExportMarkdown");
    }
}
