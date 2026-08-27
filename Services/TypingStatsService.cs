using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace TodoSidebar.Services
{
    /// <summary>
    /// 全局打字量统计服务：WH_KEYBOARD_LL 低级键盘钩子 + 分钟级批量落库。
    ///
    /// 隐私边界（设置页首启说明向用户承诺，代码层面可审计验证）：
    ///  ① 只统计数量（击键数/估算字数），任何情况下不存储按键序列、不存储文本内容；
    ///  ② 默认关闭——用户在设置页显式开启后才安装钩子；关闭即卸载钩子停止计数；
    ///  ③ 数据仅写入本地 SQLite（DailyTypingStat），不参与云同步、不上传。
    ///
    /// 实时性约束：WH_KEYBOARD_LL 回调必须微秒级返回，否则拖慢全系统输入——
    /// 回调内只做查表 + 纯内存累加，绝不触碰磁盘/网络。
    /// 所有事件经由安装线程（UI 线程消息泵）回调，与聚合定时器同线程，天然免锁。
    /// </summary>
    public sealed class TypingStatsService : IDisposable
    {
        // ===== Win32 =====

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetKeyboardLayout(uint idThread);

        [DllImport("imm32.dll")]
        private static extern bool ImmIsIME(IntPtr hKL);

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        // ===== 单例 =====

        private static readonly Lazy<TypingStatsService> _lazy = new(() => new TypingStatsService());
        public static TypingStatsService Instance => _lazy.Value;

        private readonly TypingCounterCore _core = new();
        private DispatcherTimer? _flushTimer;
        private IntPtr _hook = IntPtr.Zero;
        private LowLevelKeyboardProc? _proc; // 防 GC 回收委托

        /// <summary>当前是否处于启用状态（钩子已安装并在计数）。</summary>
        public bool IsEnabled { get; private set; }

        private TypingStatsService() { }

        /// <summary>
        /// 开关入口（设置页勾选 / 启动恢复）。开关即时生效，无需重启；
        /// 关闭时立即冲刷增量落库并卸载系统钩子。
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            if (enabled == IsEnabled) return;

            if (enabled)
            {
                InstallHook();
                if (IsEnabled)
                {
                    EnsureBaseline();          // R61：装载当日基线，实时显示立即可用
                    _flushTimer ??= CreateFlushTimer();
                    _flushTimer.Start();
                }
            }
            else
            {
                FlushNow();                 // 关闭前把残余增量写库
                if (_flushTimer != null) _flushTimer.Stop();
                UninstallHook();
            }
        }

        private DispatcherTimer CreateFlushTimer()
        {
            var t = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(45)
            };
            t.Tick += (_, _) => FlushNow();
            return t;
        }

        private void InstallHook()
        {
            if (_hook != IntPtr.Zero) { IsEnabled = true; return; }
            try
            {
                _proc = HookProc;
                _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
                IsEnabled = _hook != IntPtr.Zero;
                if (!IsEnabled)
                    System.Diagnostics.Debug.WriteLine("[TypingStats] 钩子安装失败");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TypingStats] 钩子安装异常: {ex.Message}");
                IsEnabled = false;
            }
        }

        private void UninstallHook()
        {
            if (_hook != IntPtr.Zero)
            {
                try { UnhookWindowsHookEx(_hook); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[TypingStats] 钩子卸载异常: {ex.Message}"); }
                _hook = IntPtr.Zero;
            }
            _proc = null;
            IsEnabled = false;
        }

        // ===== 当日基线（R61 实时显示）=====
        // 基线 = 服务启用当天从 DB 读到的已落库值；实时总数 = 基线 + 内存累计。
        // 跨天时先把旧日增量冲刷进旧日期键，再切换到新日的零基线。
        private string? _todayKey;
        private long _baseKeys;
        private long _baseWords;

        private static string KeyOf(DateTime d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>保证基线属于今天；跨天则先落库旧日增量再读新日基线。</summary>
        private void EnsureBaseline()
        {
            var today = KeyOf(DateTime.Today);
            if (_todayKey == today) return;

            if (_todayKey != null)
            {
                var (dk, dw) = _core.TakeDelta();
                if (dk > 0 || dw > 0)
                {
                    DatabaseService.Instance.AddTypingStat(_todayKey,
                        checked((int)Math.Min(dk, int.MaxValue)),
                        checked((int)Math.Min(dw, int.MaxValue)));
                }
            }

            _todayKey = today;
            var stat = DatabaseService.Instance.GetTypingStat(today);
            _baseKeys = stat.KeyStrokes;
            _baseWords = stat.WordChars;
        }

        /// <summary>
        /// R61 实时显示入口：今日累计（基线 + 内存中的当前计数），不产生任何写库。
        /// UI 层 1 秒轮询此值即可实现秒级实时刷新。
        /// </summary>
        public (int keys, int words) GetLiveTotals()
        {
            try
            {
                if (!IsEnabled)
                    return DatabaseService.Instance.GetTypingStat(KeyOf(DateTime.Today));

                EnsureBaseline();
                var (k, w) = _core.Peek();
                return (checked((int)Math.Min(_baseKeys + k, int.MaxValue)),
                        checked((int)Math.Min(_baseWords + w, int.MaxValue)));
            }
            catch
            {
                return (0, 0);
            }
        }

        /// <summary>把当前累计增量按今日日期键落库，并同步抬高基线避免双重计算。失败静默。</summary>
        public void FlushNow()
        {
            try
            {
                if (_todayKey == null && !IsEnabled) return; // 从未启用过：无事可做
                EnsureBaseline();
                var (keys, words) = _core.TakeDelta();
                if (keys == 0 && words == 0) return;
                DatabaseService.Instance.AddTypingStat(
                    _todayKey!,
                    checked((int)Math.Min(keys, int.MaxValue)),
                    checked((int)Math.Min(words, int.MaxValue)));
                _baseKeys += keys;
                _baseWords += words;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TypingStats] 落库失败: {ex.Message}");
            }
        }

        // ===== 键盘映射 =====

        private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                try
                {
                    var kbStruct = Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                if (kbStruct is KBDLLHOOKSTRUCT kb)
                {
                    HandleKey(kb.vkCode);
                }
                }
                catch
                {
                    // 统计异常绝不能干扰输入链路
                }
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        private void HandleKey(uint vkRaw)
        {
            int vk = (int)vkRaw;

            // 非打字类按键不计数：F1–F24、修饰键（Ctrl/Alt/Win，Shift 单独按下也不计——
            // Shift+字母已在字母键上计过一次，独立计 Shift 会系统性抬高），
            // 以及导航编辑键（方向/翻页/Home/End/Ins/Del/Esc）与锁定键
            if ((vk >= 0x70 && vk <= 0x87) ||      // F1..F24
                vk == 0xA0 || vk == 0xA1 ||        // Shift L/R
                vk == 0x11 || vk == 0xA2 || vk == 0xA3 ||   // Ctrl L/R
                vk == 0x12 || vk == 0xA4 || vk == 0xA5 ||   // Alt L/R
                vk == 0x5B || vk == 0x5C ||        // Win L/R
                (vk >= 0x21 && vk <= 0x28) ||      // PgUp/PgDn/Home/End/方向键
                vk == 0x2D || vk == 0x2E ||        // Ins/Del
                vk == 0x1B ||                      // Esc
                vk == 0x14 || vk == 0x90 || vk == 0x91 || vk == 0x13)  // CapsLock/NumLock/ScrollLock/Pause
            {
                return;
            }

            switch (vk)
            {
                case 0x08: // Backspace
                    _core.OnBackspace();
                    return;

                case >= 0x41 and <= 0x5A: // A-Z
                    _core.OnAlnum((char)('a' + (vk - 0x41)), IsImeActive());
                    return;

                case >= 0x30 and <= 0x39: // 主键盘数字
                    HandleDigitOrCandidate((char)('0' + (vk - 0x30)));
                    return;

                case >= 0x60 and <= 0x69: // 小键盘数字
                    HandleDigitOrCandidate((char)('0' + (vk - 0x60)));
                    return;

                case 0x20: // Space（拼音态=空格选词上屏）
                case 0x0D: // Enter（拼音态=确认上屏）
                case 0x09: // Tab
                    _core.OnSeparator();
                    return;

                default:
                    // OEM 标点区（中英文标点都会经此断词/结算候选），以及其他罕见键按断词处理
                    if ((vk >= 0xBA && vk <= 0xC0) || (vk >= 0xDB && vk <= 0xDF))
                        _core.OnSeparator();
                    else if (!IsImeActive())
                        _core.OnSeparator();
                    // IME 组词态下的功能类按键不计入字数语义
                    return;
            }
        }

        /// <summary>数字键双语义：直输态=数字字符入段；拼音组词态=候选选择键（提交整段，估算为选词上屏）。</summary>
        private void HandleDigitOrCandidate(char digit)
        {
            if (IsImeActive())
                _core.OnSeparator();   // 组词态按数字=提交当前拼音串
            else
                _core.OnAlnum(digit, ime: false);
        }

        /// <summary>
        /// 前台窗口的键盘布局是否搭载输入法。
        /// 注意探测的是前台线程布局而非本进程——打字发生在别的程序里。
        /// 已知限制：中文布局内用 Shift 切到英文直输态时 HKL 不变，会被判为拼音态，
        /// 造成英文段被音节化拆分低估；设置页已明示 ±15% 天花板。
        /// </summary>
        private static bool IsImeActive()
        {
            try
            {
                var hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return false;
                GetWindowThreadProcessId(hwnd, out uint pid);
                var hkl = GetKeyboardLayout(pid);
                return hkl != IntPtr.Zero && ImmIsIME(hkl);
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            try
            {
                if (IsEnabled) FlushNow();
                if (_flushTimer != null) _flushTimer.Stop();
                UninstallHook();
            }
            catch
            {
                // 退出路径静默
            }
        }
    }
}
