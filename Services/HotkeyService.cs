using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace TodoSidebar.Services
{
    public class HotkeyService : IDisposable
    {
        // Win32 API
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;

        private const int WM_HOTKEY = 0x0312;

        // 热键 ID
        private const int HOTKEY_TOGGLE_SIDEBAR = 1;
        private const int HOTKEY_NEW_TASK = 2;
        private const int HOTKEY_SEARCH = 3;

        private IntPtr _windowHandle;
        private HwndSource? _source;
        private bool _isRegistered;

        /// <summary>
        /// 当前全局热键服务实例（M28：窗口切换时供新窗口迁移热键；Dispose 后置空）。
        /// </summary>
        public static HotkeyService? Current { get; private set; }

        /// <summary>
        /// 最近一次注册是否存在失败（M29：全部成功为 false，任一失败为 true，供 UI 提示热键可能不可用）。
        /// </summary>
        public bool LastRegistrationFailed { get; private set; }

        public event EventHandler? ToggleSidebarRequested;
        public event EventHandler? NewTaskRequested;
        public event EventHandler? SearchRequested;

        public void RegisterHotkeys(Window window)
        {
            // 重复注册前先注销，避免旧热键/旧 Hook 叠加导致状态不一致
            UnregisterHotkeys();

            var helper = new WindowInteropHelper(window);
            _windowHandle = helper.Handle;

            _source = HwndSource.FromHwnd(_windowHandle);
            _source?.AddHook(HwndHook);

            // M29：逐个记录注册结果，全部成功才置位 _isRegistered
            bool allSuccess = true;

            // Ctrl+Alt+T: 切换侧边栏
            if (!RegisterHotKey(_windowHandle, HOTKEY_TOGGLE_SIDEBAR, MOD_CONTROL | MOD_ALT, 0x54))
            {
                System.Diagnostics.Debug.WriteLine("[HotkeyService] Failed to register Ctrl+Alt+T");
                allSuccess = false;
            }

            // Ctrl+Alt+N: 新建任务（M29：原 Ctrl+N 会全局劫持其他程序的快捷键）
            if (!RegisterHotKey(_windowHandle, HOTKEY_NEW_TASK, MOD_CONTROL | MOD_ALT, 0x4E))
            {
                System.Diagnostics.Debug.WriteLine("[HotkeyService] Failed to register Ctrl+Alt+N");
                allSuccess = false;
            }

            // Ctrl+Alt+F: 搜索（M29：原 Ctrl+F 会全局劫持其他程序的快捷键）
            if (!RegisterHotKey(_windowHandle, HOTKEY_SEARCH, MOD_CONTROL | MOD_ALT, 0x46))
            {
                System.Diagnostics.Debug.WriteLine("[HotkeyService] Failed to register Ctrl+Alt+F");
                allSuccess = false;
            }

            // M29：按实际注册结果置位，全部成功才视为已注册；失败情况记录到 LastRegistrationFailed
            _isRegistered = allSuccess;
            LastRegistrationFailed = !allSuccess;

            // M28：暴露静态访问点，供窗口切换（侧边栏 ↔ 完整模式）时迁移热键
            Current = this;
        }

        public void UnregisterHotkeys()
        {
            // M29：部分注册失败时 _isRegistered 为 false，但已成功的热键与消息钩子仍需清理，
            // 否则重复注册会导致 Hook 叠加、热键残留；仅在从未初始化过时直接返回
            if (!_isRegistered && _windowHandle == IntPtr.Zero) return;

            UnregisterHotKey(_windowHandle, HOTKEY_TOGGLE_SIDEBAR);
            UnregisterHotKey(_windowHandle, HOTKEY_NEW_TASK);
            UnregisterHotKey(_windowHandle, HOTKEY_SEARCH);

            _source?.RemoveHook(HwndHook);
            _isRegistered = false;
        }

        // 重新注册热键到新窗口（窗口切换时调用）
        public void ReRegisterHotkeys(Window newWindow)
        {
            UnregisterHotkeys();
            RegisterHotkeys(newWindow);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();

                switch (id)
                {
                    case HOTKEY_TOGGLE_SIDEBAR:
                        ToggleSidebarRequested?.Invoke(this, EventArgs.Empty);
                        handled = true;
                        break;

                    case HOTKEY_NEW_TASK:
                        NewTaskRequested?.Invoke(this, EventArgs.Empty);
                        handled = true;
                        break;

                    case HOTKEY_SEARCH:
                        SearchRequested?.Invoke(this, EventArgs.Empty);
                        handled = true;
                        break;
                }
            }

            return IntPtr.Zero;
        }

        public void Dispose()
        {
            UnregisterHotkeys();
            // M28：服务销毁后清空静态访问点，避免外部拿到失效实例
            Current = null;
        }
    }
}
