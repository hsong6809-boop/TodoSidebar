using System;

namespace TodoSidebar.Services
{
    /// <summary>
    /// 授权层级
    /// </summary>
    public enum LicenseTier
    {
        Free,
        Pro
    }

    /// <summary>
    /// License 管理服务接口。
    /// 现阶段只搭骨架（所有人返回 Free），后期接入支付时填充实现。
    /// </summary>
    public interface ILicenseService
    {
        /// <summary>当前授权层级</summary>
        LicenseTier CurrentTier { get; }

        /// <summary>是否为 Pro 用户</summary>
        bool IsPro { get; }

        /// <summary>Pro 到期时间（null = 未激活或 Free）</summary>
        DateTime? ProExpiryDate { get; }

        /// <summary>试用期是否仍然有效</summary>
        bool IsTrialActive { get; }

        /// <summary>试用剩余天数（0 = 已过期或未开始）</summary>
        int TrialDaysRemaining { get; }

        /// <summary>
        /// 激活 License Key。
        /// 现阶段始终返回 false，后期对接 License Server。
        /// </summary>
        bool ActivateLicense(string licenseKey);

        /// <summary>
        /// 验证当前 License 是否仍然有效。
        /// 现阶段始终返回 true（Free 永远有效）。
        /// </summary>
        bool ValidateLicense();

        /// <summary>
        /// 开始 14 天试用期。
        /// 现阶段为空操作。
        /// </summary>
        void StartTrial();

        /// <summary>
        /// 清除 License 信息（降级为 Free）。
        /// </summary>
        void ClearLicense();

        /// <summary>授权层级变更事件</summary>
        event EventHandler<LicenseTier>? TierChanged;
    }
}
