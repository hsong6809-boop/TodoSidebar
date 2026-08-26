using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace TodoSidebar.Services
{
    /// <summary>
    /// 窗口视觉集成辅助（P2）：
    /// - 通过 SetWindowCompositionAttribute 启用亚克力模糊背板（Win10 1809+ / Win11），
    ///   让"毛玻璃"从半透明纯色升级为真实背景模糊；
    /// - 全程静默降级：任何 API 失败仅返回 false，窗口保留现有半透明外观，不影响功能。
    /// 可通过设置项 AcrylicEnabled = false 关闭。
    /// </summary>
    public static class DwmBackdropHelper
    {
        private const int WCA_ACCENT_POLICY = 19;
        private const int ACCENT_DISABLED = 0;
        private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;
        // 最小可用版本：Windows 10 1809 (build 17763) 的亚克力才基本稳定
        private const int MinAcrylicBuild = 17763;

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public uint AccentFlags;
            public uint GradientColor;   // ABGR
            public uint AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        /// <summary>
        /// 为主外壳窗口应用亚克力背板（读取设置开关 + 当前主题底色）。
        /// 成功时会覆盖该窗口的 GlassBrush 为低不透明度版本，让模糊可见；失败静默跳过。
        /// 注意：默认关闭（部分显卡/RDP 环境下分层窗口的 Accent 亚克力会渲染成全透明或黑块），
        /// 需要在数据库设置中写入 AcrylicEnabled = true 显式开启。
        /// </summary>
        public static void ApplyMainShellAcrylic(Window window)
        {
            try
            {
                if (window == null) return;

                bool enabled;
                try { enabled = DatabaseService.Instance.GetSetting("AcrylicEnabled") == "true"; }
                catch { enabled = false; }
                if (!enabled) return;

                var tint = ThemeManager.IsCurrentlyDark()
                    ? Color.FromRgb(0x0F, 0x17, 0x2A)   // Slate-900
                    : Color.FromRgb(0xF8, 0xF9, 0xFE);  // 近白蓝灰

                if (!TryEnableAcrylic(window, tint, 0x50)) return;

                // 背板生效：降低窗体主面板不透明度（仅该窗口资源作用域）
                window.Resources["GlassBrush"] = new SolidColorBrush(Color.FromArgb(0x5A, tint.R, tint.G, tint.B));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DwmBackdropHelper: 应用亚克力失败: {ex.Message}");
            }
        }

        /// <summary>启用亚克力模糊。tint 为叠加在模糊上的底色，alpha 为其不透明度。</summary>
        public static bool TryEnableAcrylic(Window window, Color tint, byte alpha)
        {
            try
            {
                if (window == null) return false;
                if (Environment.OSVersion.Version.Build < MinAcrylicBuild) return false;

                var hwnd = new WindowInteropHelper(window).EnsureHandle();
                if (hwnd == IntPtr.Zero) return false;

                // R50 修复（审查 L1）：GradientColor 惯例为 0xAARRGGBB——
                // 原实现 R/B 通道写反，开启亚克力后底色红蓝互换（深色主题变暖棕、浅色偏粉）
                uint abgr = ((uint)alpha << 24) | ((uint)tint.R << 16) | ((uint)tint.G << 8) | tint.B;
                var accent = new AccentPolicy
                {
                    AccentState = ACCENT_ENABLE_ACRYLICBLURBEHIND,
                    AccentFlags = 2,
                    GradientColor = abgr
                };

                var accentPtr = Marshal.AllocHGlobal(Marshal.SizeOf<AccentPolicy>());
                try
                {
                    Marshal.StructureToPtr(accent, accentPtr, false);
                    var data = new WindowCompositionAttributeData
                    {
                        Attribute = WCA_ACCENT_POLICY,
                        Data = accentPtr,
                        SizeOfData = Marshal.SizeOf<AccentPolicy>()
                    };
                    return SetWindowCompositionAttribute(hwnd, ref data) != 0;
                }
                finally
                {
                    Marshal.FreeHGlobal(accentPtr);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DwmBackdropHelper: TryEnableAcrylic 异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>关闭窗口的合成效果（预留：用于设置开关切换）。</summary>
        public static void Disable(Window window)
        {
            try
            {
                if (window == null) return;
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return;

                var accent = new AccentPolicy { AccentState = ACCENT_DISABLED };
                var accentPtr = Marshal.AllocHGlobal(Marshal.SizeOf<AccentPolicy>());
                try
                {
                    Marshal.StructureToPtr(accent, accentPtr, false);
                    var data = new WindowCompositionAttributeData
                    {
                        Attribute = WCA_ACCENT_POLICY,
                        Data = accentPtr,
                        SizeOfData = Marshal.SizeOf<AccentPolicy>()
                    };
                    SetWindowCompositionAttribute(hwnd, ref data);
                }
                finally
                {
                    Marshal.FreeHGlobal(accentPtr);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DwmBackdropHelper: Disable 异常: {ex.Message}");
            }
        }
    }
}
