namespace TodoSidebar.Services
{
    /// <summary>
    /// Feature Flag 服务接口。
    /// 控制 Pro 功能的可见性和可用性。
    /// 基于 ILicenseService 动态判断：Free 用户所有 Pro 功能禁用。
    /// </summary>
    public interface IFeatureFlagService
    {
        /// <summary>
        /// 检查指定功能是否启用。
        /// Pro 功能：取决于 License；普通功能：始终启用。
        /// </summary>
        bool IsEnabled(string featureKey);

        /// <summary>
        /// 检查指定功能是否为 Pro 专属。
        /// </summary>
        bool IsProFeature(string featureKey);

        /// <summary>
        /// 手动设置 flag（覆盖 License 判断，用于测试或特殊场景）。
        /// </summary>
        void SetFlag(string featureKey, bool enabled);

        // === 预定义功能 Key ===

        /// <summary>云同步</summary>
        bool CloudSync { get; }

        /// <summary>高级搜索（全文搜索、筛选器、保存搜索条件）</summary>
        bool AdvancedSearch { get; }

        /// <summary>标签系统</summary>
        bool TagSystem { get; }

        /// <summary>数据统计（完成率、趋势图、周报/月报）</summary>
        bool Statistics { get; }

        /// <summary>自定义主题（颜色、字体、透明度）</summary>
        bool CustomThemes { get; }

        /// <summary>无限模板</summary>
        bool UnlimitedTemplates { get; }

        /// <summary>CSV 导出</summary>
        bool DataExportCsv { get; }

        /// <summary>Markdown 导出</summary>
        bool DataExportMarkdown { get; }
    }
}
