using System;

namespace TodoSidebar.Services
{
    /// <summary>
    /// License 管理服务 — 初始骨架实现。
    /// 所有人都是 Free，Trial 逻辑为空。后期接入支付时填充。
    /// </summary>
    public class LicenseService : ILicenseService
    {
        private LicenseTier _currentTier = LicenseTier.Free;
        private DateTime? _trialStartDate;

        public LicenseTier CurrentTier => _currentTier;
        public bool IsPro => _currentTier == LicenseTier.Pro;
        public DateTime? ProExpiryDate => null; // 后期对接 License Server

        public bool IsTrialActive =>
            _trialStartDate.HasValue &&
            _currentTier == LicenseTier.Free &&
            (DateTime.Now - _trialStartDate.Value).TotalDays < 14;

        public int TrialDaysRemaining
        {
            get
            {
                if (!_trialStartDate.HasValue || _currentTier == LicenseTier.Pro)
                    return 0;
                var remaining = 14 - (int)(DateTime.Now - _trialStartDate.Value).TotalDays;
                return Math.Max(0, remaining);
            }
        }

        public bool ActivateLicense(string licenseKey)
        {
            // 后期实现：HTTP POST 到 License Server 验证
            // 现阶段：始终返回 false
            System.Diagnostics.Debug.WriteLine($"[LicenseService] ActivateLicense called with key: {licenseKey?.Substring(0, Math.Min(8, licenseKey?.Length ?? 0))}...");
            return false;
        }

        public bool ValidateLicense()
        {
            // Free 永远有效
            if (_currentTier == LicenseTier.Free)
                return true;

            // 后期实现：在线验证 + 离线缓存
            return true;
        }

        public void StartTrial()
        {
            if (!_trialStartDate.HasValue)
            {
                _trialStartDate = DateTime.Now;
                System.Diagnostics.Debug.WriteLine("[LicenseService] Trial started");
            }
        }

        public void ClearLicense()
        {
            var changed = _currentTier != LicenseTier.Free;
            _currentTier = LicenseTier.Free;
            _trialStartDate = null;
            if (changed)
                TierChanged?.Invoke(this, LicenseTier.Free);
        }

        public event EventHandler<LicenseTier>? TierChanged;
    }
}
