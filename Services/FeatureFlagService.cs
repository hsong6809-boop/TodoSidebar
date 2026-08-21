using System.Collections.Generic;

namespace TodoSidebar.Services
{
    /// <summary>
    /// Feature Flag 服务 — 基于 ILicenseService 动态判断。
    /// L14 修复：当前阶段所有 Pro 功能对 Free 用户开放（许可/支付服务尚未接入），
    /// 接许可服务时再收紧为"Free 用户禁用 Pro 功能"（见 IsEnabled 内注释）。
    /// </summary>
    public class FeatureFlagService : IFeatureFlagService
    {
        // L14 修复：预留字段 — 接入许可服务后用于按授权收紧 Pro 功能；
        // 当前 IsEnabled 暂未消费，保留注入以免破坏构造函数签名
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

            // 商业化策略（当前阶段）：许可/支付服务尚未接入，所有功能对所有用户开放。
            // 接入许可服务后，改为以下判定：
            //   return _licenseService.IsPro || !IsProFeature(featureKey);
            // 同时将 IFeatureFlagService 的注释契约同步为"Free 用户禁用 Pro 功能"。
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
