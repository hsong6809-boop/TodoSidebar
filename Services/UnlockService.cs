using System;
using TodoSidebar.Models;

namespace TodoSidebar.Services
{
    /// <summary>
    /// v5.5 等级解锁体系：
    ///   强调色 —— Indigo/Mono 免费，Ocean Lv3 / Sunset Lv5 / Forest Lv8；
    ///   内置头像 —— d1~d3 免费，d4→Lv4 / d5→Lv6 / d6→Lv8 / d7→Lv10 / d8→Lv12；
    ///   自定义头像 —— 预留 Pro 权益开关（LicenseService），当前版本默认开放。
    /// 兼容原则：已选中的强调色即使未达等级也不回收，仅限制"更换到未解锁项"。
    /// </summary>
    public static class UnlockService
    {
        private static int _cachedLevel = 1;
        private static bool _loaded;

        private static int Level
        {
            get
            {
                if (_loaded) return _cachedLevel;
                try { _cachedLevel = Math.Max(1, LevelService.Instance.GetGrowth().Level); }
                catch { _cachedLevel = 1; }
                _loaded = true;
                return _cachedLevel;
            }
        }

        /// <summary>升级后调用，使下次判定取最新等级。</summary>
        public static void RefreshLevel() => _loaded = false;

        // ===== 强调色门槛 =====

        /// <summary>强调色解锁等级；免费项返回 0。未知名返回 0（不误伤）。</summary>
        public static int AccentRequiredLevel(string accentName) => accentName switch
        {
            "Ocean" => 3,
            "Sunset" => 5,
            "Forest" => 8,
            _ => 0,
        };

        /// <summary>内置头像解锁等级；d1~d3 免费。</summary>
        public static int AvatarRequiredLevel(string? avatarKind) => NormalizeKind(avatarKind) switch
        {
            "d4" => 4,
            "d5" => 6,
            "d6" => 8,
            "d7" => 10,
            "d8" => 12,
            _ => 0,
        };

        /// <summary>该强调色是否可选用（含"当前已在用"的兼容豁免）。</summary>
        public static bool IsAccentUnlocked(string accentName, string? currentAccent = null)
        {
            var req = AccentRequiredLevel(accentName);
            if (req <= 0) return true;
            if (string.Equals(accentName, currentAccent, StringComparison.OrdinalIgnoreCase)) return true;
            return Level >= req;
        }

        /// <summary>该头像是否可选用。</summary>
        public static bool IsAvatarUnlocked(string? avatarKind, string? currentKind = null)
        {
            var req = AvatarRequiredLevel(avatarKind);
            if (req <= 0) return true;
            var kind = NormalizeKind(avatarKind);
            if (string.Equals(kind, currentKind, StringComparison.OrdinalIgnoreCase)) return true;
            return Level >= req;
        }

        /// <summary>自定义头像是否可用（预留 Pro 开关；当前策略 Free 亦开放）。</summary>
        public static bool IsCustomAvatarAllowed()
        {
            try
            {
                // 未来切换 Pro 门槛：return App.Services?.GetService(typeof(ILicenseService)) is ILicenseService l && l.IsPro;
                return true;
            }
            catch { return true; }
        }

        internal static string NormalizeKind(string? kind)
        {
            var k = (kind ?? "").Trim().ToLowerInvariant();
            return k.Length == 2 && k[0] == 'd' && char.IsDigit(k[1]) ? k : "";
        }
    }
}
