using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TodoSidebar.Helpers
{
    public static class BlurHelper
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS
        {
            public int cxLeftWidth;
            public int cxRightWidth;
            public int cyTopHeight;
            public int cyBottomHeight;
        }

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMSBT_MAINWINDOW = 2; // Mica
        private const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic

        // 圆角偏好
        private const int DWMWCP_DEFAULT = 0;
        private const int DWMWCP_DONOTROUND = 1;
        private const int DWMWCP_ROUND = 2;
        private const int DWMWCP_ROUNDSMALL = 3;

        /// <summary>
        /// 为窗口启用 Acrylic 毛玻璃效果（Windows 11）
        /// </summary>
        public static void EnableAcrylicBlur(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;

            // 启用暗色模式
            int darkMode = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

            // 设置 Acrylic 效果
            int backdropType = DWMSBT_TRANSIENTWINDOW;
            int result = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));

            // 如果 Acrylic 不可用（非 Windows 11），尝试 Mica
            if (result != 0)
            {
                backdropType = DWMSBT_MAINWINDOW;
                DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));
            }

            // 扩展框架到客户区
            var margins = new MARGINS
            {
                cxLeftWidth = -1,
                cxRightWidth = -1,
                cyTopHeight = -1,
                cyBottomHeight = -1
            };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);
        }

        /// <summary>
        /// 设置窗口圆角（Windows 11）
        /// </summary>
        public static void SetRoundCorners(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            int cornerPreference = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));
        }

        /// <summary>
        /// 设置窗口不使用圆角
        /// </summary>
        public static void SetNoRoundCorners(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            int cornerPreference = DWMWCP_DONOTROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));
        }
    }
}
