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

        public event EventHandler? ToggleSidebarRequested;
        public event EventHandler? NewTaskRequested;
        public event EventHandler? SearchRequested;

        public void RegisterHotkeys(Window window)
        {
            var helper = new WindowInteropHelper(window);
            _windowHandle = helper.Handle;

            _source = HwndSource.FromHwnd(_windowHandle);
            _source?.AddHook(HwndHook);

            // Ctrl+Alt+T: 切换侧边栏
            if (!RegisterHotKey(_windowHandle, HOTKEY_TOGGLE_SIDEBAR, MOD_CONTROL | MOD_ALT, 0x54))
                System.Diagnostics.Debug.WriteLine("[HotkeyService] Failed to register Ctrl+Alt+T");

            // Ctrl+N: 新建任务
            if (!RegisterHotKey(_windowHandle, HOTKEY_NEW_TASK, MOD_CONTROL, 0x4E))
                System.Diagnostics.Debug.WriteLine("[HotkeyService] Failed to register Ctrl+N");

            // Ctrl+F: 搜索
            if (!RegisterHotKey(_windowHandle, HOTKEY_SEARCH, MOD_CONTROL, 0x46))
                System.Diagnostics.Debug.WriteLine("[HotkeyService] Failed to register Ctrl+F");

            _isRegistered = true;
        }

        public void UnregisterHotkeys()
        {
            if (!_isRegistered) return;

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
        }
    }
}
