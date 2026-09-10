using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TodoSidebar.Controls;
using TodoSidebar.Services;

namespace TodoSidebar
{
    /// <summary>
    /// v5.7 第三形态「悬浮球」：胶囊/圆形两种形状的置顶小挂件。
    /// - 胶囊：左侧今日进度环 + 右侧轮换信息（接下来/今日/输入/连击），番茄进行中优先倒计时
    /// - 圆形：进度环 + 中心今日完成数
    /// 交互：单击翻页、双击进完整模式、拖动换位（位置记忆）、右键菜单、闲置自动淡化。
    /// 不抢焦点：WM_MOUSEACTIVATE 返回 MA_NOACTIVATE，点击/拖动均不夺走当前应用焦点。
    /// </summary>
    public partial class WidgetWindow : Window
    {
        /// <summary>当前悬浮球实例（保证全应用唯一，模式切换/设置联动共用）。</summary>
        private static WidgetWindow? _current;

        /// <summary>设置面板改了悬浮球相关选项后调用，让已打开的实例即时生效。</summary>
        public static void ApplySettingsChanged() => _current?.RefreshFromSettings();

        // ===== 设置缓存（RefreshFromSettings 统一重读） =====
        private string _shape = "pill";
        private string[] _itemKeys = { "next" };
        private int _intervalSec = 6;
        private bool _idleFade = true;
        private bool _locked;

        // ===== 运行状态 =====
        private DispatcherTimer? _rotateTimer;
        private DispatcherTimer? _fastTimer;
        private DispatcherTimer? _idleTimer;
        private DispatcherTimer? _topmostTimer;
        private DispatcherTimer? _savePosTimer;
        private DispatcherTimer? _flipTimer;
        private DateTime _lastActivity = DateTime.Now;
        private int _rotateIndex;
        private bool _textAnimating;
        private const int IdleFadeSeconds = 20;
        private const double IdleOpacity = 0.35;

        public WidgetWindow()
        {
            InitializeComponent();
            _current = this;
            RefreshFromSettings();
        }

        #region 设置读取与布局

        /// <summary>从 settings 表重读全部悬浮球设置并应用（构造与设置面板变更时调用）。</summary>
        private void RefreshFromSettings()
        {
            var db = DatabaseService.Instance;
            try { _shape = db.GetSetting("WidgetShape") == "circle" ? "circle" : "pill"; }
            catch { _shape = "pill"; }

            // 轮换内容：默认只开「接下来」；空列表回退
            try
            {
                var raw = db.GetSetting("WidgetItems");
                if (!string.IsNullOrWhiteSpace(raw))
                    _itemKeys = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                   .Where(k => k is "next" or "today" or "typing" or "combo").ToArray();
                if (_itemKeys.Length == 0) _itemKeys = new[] { "next" };
            }
            catch { _itemKeys = new[] { "next" }; }

            try
            {
                _intervalSec = Math.Clamp(int.TryParse(db.GetSetting("WidgetInterval"), out var sec) ? sec : 6, 3, 60);
            }
            catch { _intervalSec = 6; }

            try { _idleFade = db.GetSetting("WidgetIdleFade") != "false"; } catch { _idleFade = true; }
            try { _locked = db.GetSetting("WidgetLocked") == "true"; } catch { _locked = false; }

            ApplyShape();
            ClampToWorkArea();
            SyncMenuChecks();
            UpdateDisplay(animate: false);

            // 轮换间隔可能变化，重建定时器
            if (_rotateTimer != null)
            {
                _rotateTimer.Interval = TimeSpan.FromSeconds(_intervalSec);
                _rotateTimer.Start();
            }
        }

        /// <summary>按形状设置切换窗口尺寸与可见面板。</summary>
        private void ApplyShape()
        {
            if (_shape == "circle")
            {
                Width = 80; Height = 80;
                CircleRoot.Visibility = Visibility.Visible;
                PillRoot.Visibility = Visibility.Collapsed;
            }
            else
            {
                Width = 212; Height = 64;
                PillRoot.Visibility = Visibility.Visible;
                CircleRoot.Visibility = Visibility.Collapsed;
            }
        }

        private void SyncMenuChecks()
        {
            if (MenuLock != null) MenuLock.IsChecked = _locked;
            if (MenuIdleFade != null) MenuIdleFade.IsChecked = _idleFade;
        }

        #endregion

        #region 信息项（轮换）

        /// <summary>单条信息（标签, 数值）；返回 null 表示当前不可用（轮换时跳过）。</summary>
        private (string Label, string Value)? ProvideItem(string key)
        {
            var vm = App.SharedViewModel;
            switch (key)
            {
                case "next":
                    if (vm == null) return null;
                    return ("接下来", vm.NextTaskTitle);
                case "today":
                    return vm == null ? null : ("今日", vm.TodayDoneText);
                case "typing":
                    try
                    {
                        if (DatabaseService.Instance.GetSetting("TypingStatsEnabled") != "true") return null;
                        var (k, w) = TypingStatsService.Instance.GetLiveTotals();
                        return ("输入", k == 0 && w == 0
                            ? "今天还没有输入"
                            : $"约 {w.ToString("N0", CultureInfo.InvariantCulture)} 字");
                    }
                    catch { return null; }
                case "combo":
                    try
                    {
                        var c = LevelService.Instance.GetGrowth().ComboDays;
                        return c > 0 ? ("连击", $"🔥 x{c}") : null;
                    }
                    catch { return null; }
            }
            return null;
        }

        /// <summary>可用项快照：跳过 null 项；全空时回退 today（today 恒有值）。单次快照避免重复取值。</summary>
        private (string[] Keys, Dictionary<string, (string Label, string Value)> Values) SnapshotItems()
        {
            var values = new Dictionary<string, (string, string)>();
            var keys = new List<string>();
            foreach (var k in _itemKeys)
            {
                var v = ProvideItem(k);
                if (v.HasValue) { keys.Add(k); values[k] = v.Value; }
            }
            if (keys.Count == 0)
            {
                keys.Add("today");
                values["today"] = ProvideItem("today") ?? ("今日", "0 / 0");
            }
            return (keys.ToArray(), values);
        }

        /// <summary>轮换索引推进（番茄运行时由优先显示逻辑覆盖）。</summary>
        private void AdvanceRotation()
        {
            var (keys, _) = SnapshotItems();
            _rotateIndex = (_rotateIndex + 1) % keys.Length;
            UpdateDisplay(animate: true);
        }

        #endregion

        #region 渲染

        /// <summary>刷新文字与进度环。番茄非空闲时优先显示倒计时（重要信息插队）。</summary>
        private void UpdateDisplay(bool animate)
        {
            var vm = App.SharedViewModel;
            var pomo = PomodoroService.Instance;
            (string Label, string Value) item;

            if (pomo.State is PomodoroState.Focus or PomodoroState.Paused or PomodoroState.Break)
            {
                var time = PomodoroService.FormatTime(pomo.RemainingSeconds);
                item = pomo.State switch
                {
                    PomodoroState.Focus => ("🍅 专注", time),
                    PomodoroState.Paused => ("⏸ 暂停", time),
                    _ => ("☕ 休息", time)
                };
            }
            else
            {
                var (keys, values) = SnapshotItems();
                if (_rotateIndex >= keys.Length) _rotateIndex = 0;
                var key = keys[_rotateIndex];
                item = values.TryGetValue(key, out var v) ? v : ("—", "");
            }

            SetWidgetText(item.Label, item.Value, animate);
            UpdateRing();
        }

        /// <summary>今日进度环（含超额转金彩蛋）；圆形形态中心同步今日完成数。</summary>
        private void UpdateRing()
        {
            var vm = App.SharedViewModel;
            double progress = 0;
            bool over = false;
            if (vm != null) { progress = Math.Clamp(vm.TodayProgressRate, 0, 1); over = vm.IsOverachieving; }

            var brush = over
                ? FindResource("WarningBrush") as System.Windows.Media.Brush
                : FindResource("AccentGradientBrush") as System.Windows.Media.Brush;
            var accent = over
                ? FindResource("WarningBrush") as System.Windows.Media.Brush
                : FindResource("AccentBrush") as System.Windows.Media.Brush;

            if (PillRing.Progress != progress) PillRing.Progress = progress;
            if (brush != null && !ReferenceEquals(PillRing.ProgressBrush, brush)) PillRing.ProgressBrush = brush;
            if (CircleRing.Progress != progress) CircleRing.Progress = progress;
            if (brush != null && !ReferenceEquals(CircleRing.ProgressBrush, brush)) CircleRing.ProgressBrush = brush;

            var percent = $"{(int)Math.Round(progress * 100)}%";
            if (PillPercentText.Text != percent)
            {
                PillPercentText.Text = percent;
                if (accent != null) PillPercentText.Foreground = accent;
            }

            var centerText = vm?.TodayDoneText ?? "0 / 0";
            if (CircleValue.Text != centerText) CircleValue.Text = centerText;
        }

        /// <summary>标签+数值整体更新：变化时淡出→换字→淡入，静止时直接换。</summary>
        private void SetWidgetText(string label, string value, bool animate)
        {
            if (PillLabel.Text == label && PillValue.Text == value) return;
            if (animate && !AnimationService.ReduceMotion && !_textAnimating)
            {
                _textAnimating = true;
                var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150));
                fadeOut.Completed += (_, _) =>
                {
                    PillLabel.Text = label;
                    PillValue.Text = value;
                    var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(150));
                    fadeIn.Completed += (_, _) =>
                    {
                        PillContent.BeginAnimation(OpacityProperty, null);
                        PillContent.Opacity = 1;
                        _textAnimating = false;
                    };
                    PillContent.BeginAnimation(OpacityProperty, fadeIn);
                };
                PillContent.BeginAnimation(OpacityProperty, fadeOut);
            }
            else
            {
                PillContent.BeginAnimation(OpacityProperty, null);
                PillContent.Opacity = 1;
                PillLabel.Text = label;
                PillValue.Text = value;
                _textAnimating = false;
            }
        }

        #endregion

        #region 生命周期

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            RestorePosition();

            // v5.7：真实亚克力背板（受 AcrylicEnabled 设置门控，失败静默降级为半透明纯色）
            DwmBackdropHelper.ApplyMainShellAcrylic(this);

            // 番茄钟联动：状态/完成事件触发刷新。
            // 审查 P2：倒计时秒级跳动已由下方每秒 _fastTimer 覆盖，原先再订阅
            // PomodoroService.Tick 会让 UpdateDisplay 每秒执行两遍（冗余刷新），去除。
            PomodoroService.Instance.StateChanged += OnPomoStateChanged;
            PomodoroService.Instance.SessionCompleted += OnPomoSessionCompleted;

            // 轮换定时器
            _rotateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_intervalSec) };
            _rotateTimer.Tick += (_, _) => AdvanceRotation();
            _rotateTimer.Start();

            // 1 秒快刷：倒计时/输入字数/进度环实时
            _fastTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _fastTimer.Tick += (_, _) => UpdateDisplay(animate: false);
            _fastTimer.Start();

            // 闲置淡化巡检
            _idleTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(3) };
            _idleTimer.Tick += (_, _) => CheckIdleFade();
            _idleTimer.Start();

            // 位置记忆防抖
            _savePosTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _savePosTimer.Tick += (_, _) => { _savePosTimer.Stop(); SavePosition(); };

            // 延迟翻页（审查 P2）：单击翻页延后 240ms 执行，双击展开会把它取消，
            // 避免"双击展开完整窗口前先闪一次翻页"的副作用
            _flipTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(240) };
            _flipTimer.Tick += (_, _) => { _flipTimer!.Stop(); AdvanceRotation(); };

            // 置顶保持（与侧边栏同策略）
            _topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _topmostTimer.Tick += (_, _) => ReAssertTopmost();
            _topmostTimer.Start();
            ReAssertTopmost();

            LocationChanged += (_, _) =>
            {
                if (!_positionLoaded) return;
                _lastActivity = DateTime.Now;
                // 位置防抖：停止再启动，500ms 内连续移动只存最后一次
                if (_savePosTimer.IsEnabled) _savePosTimer.Stop();
                _savePosTimer.Start();
            };

            _positionLoaded = true;
        }

        private bool _positionLoaded;

        private void OnPomoStateChanged(object? sender, PomodoroState state) => UpdateDisplay(animate: true);
        private void OnPomoSessionCompleted(object? sender, PomodoroSessionCompletedEventArgs e) => UpdateDisplay(animate: true);

        private void Window_Closed(object? sender, EventArgs e)
        {
            if (ReferenceEquals(_current, this)) _current = null;
            PomodoroService.Instance.StateChanged -= OnPomoStateChanged;
            PomodoroService.Instance.SessionCompleted -= OnPomoSessionCompleted;
            _rotateTimer?.Stop();
            _fastTimer?.Stop();
            _idleTimer?.Stop();
            _topmostTimer?.Stop();
            _savePosTimer?.Stop();
            _flipTimer?.Stop();
        }

        #endregion

        #region 不抢焦点 / 置顶保持

        private HwndSourceHook? _hook;

        private void Window_SourceInitialized(object? sender, EventArgs e)
        {
            // WM_MOUSEACTIVATE → MA_NOACTIVATE：点击悬浮球不激活窗口，
            // 全屏游戏/视频不会被切前台，也不会触发其他窗口的 Deactivated
            _hook = (IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam, ref bool handled) =>
            {
                if (msg == 0x0021) // WM_MOUSEACTIVATE
                {
                    handled = true;
                    return (IntPtr)3; // MA_NOACTIVATE
                }
                return IntPtr.Zero;
            };
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source?.AddHook(_hook);
        }

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HwndTopmost = new(-1);
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoActivate = 0x0010;

        /// <summary>每 3 秒重申 HWND_TOPMOST（侧边栏同款策略，防全屏置顶应用压住）。</summary>
        private void ReAssertTopmost()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;
                SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
                if (!Topmost) Topmost = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Widget ReAssertTopmost error: {ex.Message}");
            }
        }

        #endregion

        #region 交互（拖动/单击翻页/双击进完整模式）

        private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _lastActivity = DateTime.Now;

            // 双击：展开完整窗口（快速连点时第二次按下 ClickCount==2）。
            // 同时取消第一次按下排队的翻页，避免"展开前先闪一页"
            if (e.ClickCount >= 2)
            {
                e.Handled = true;
                _flipTimer?.Stop();
                OpenFullMode();
                return;
            }

            if (_locked)
            {
                // 锁定位置时单击仍可手动翻页（同样延迟，给双击留取消窗口）
                ScheduleFlip();
                return;
            }

            var beforeLeft = Left;
            var beforeTop = Top;
            var downAt = DateTime.Now;
            try { DragMove(); }
            catch { return; }

            // DragMove 返回 = 已松开。位移极小且按压短 → 视为单击，排入延迟翻页
            var moved = Math.Abs(Left - beforeLeft) + Math.Abs(Top - beforeTop);
            if (moved < 4 && (DateTime.Now - downAt).TotalMilliseconds < 500)
                ScheduleFlip();
        }

        /// <summary>把「单击翻页」延后执行；240ms 窗口内若发生双击会被取消。</summary>
        private void ScheduleFlip()
        {
            if (_flipTimer == null) { AdvanceRotation(); return; }
            _flipTimer.Stop();
            _flipTimer.Start();
        }

        private void Root_MouseEnter(object sender, MouseEventArgs e)
        {
            _lastActivity = DateTime.Now;
            RestoreOpacity();
        }

        private void Root_MouseMove(object sender, MouseEventArgs e) => _lastActivity = DateTime.Now;

        private void OpenFullMode()
        {
            try { App.SwitchDisplayMode(App.AppDisplayMode.Full); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Widget open full error: {ex.Message}");
            }
        }

        #endregion

        #region 闲置淡化

        private void CheckIdleFade()
        {
            if (!_idleFade || IsMouseOver) return;
            if ((DateTime.Now - _lastActivity).TotalSeconds < IdleFadeSeconds) return;
            if (Opacity > IdleOpacity)
            {
                var fade = new DoubleAnimation(IdleOpacity, TimeSpan.FromMilliseconds(600));
                BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
            }
        }

        private void RestoreOpacity()
        {
            if (Opacity >= 1) return;
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
        }

        #endregion

        #region 位置记忆

        /// <summary>恢复记忆位置（越界钳回工作区）；无记忆时默认屏幕右下角。</summary>
        private void RestorePosition()
        {
            var work = SystemParameters.WorkArea;
            try
            {
                var db = DatabaseService.Instance;
                var lRaw = db.GetSetting("WidgetLeft");
                var tRaw = db.GetSetting("WidgetTop");
                if (!string.IsNullOrEmpty(lRaw) && !string.IsNullOrEmpty(tRaw)
                    && double.TryParse(lRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var l)
                    && double.TryParse(tRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var t))
                {
                    Left = l;
                    Top = t;
                }
                else
                {
                    Left = work.Right - Width - 16;
                    Top = work.Bottom - Height - 16;
                }
            }
            catch
            {
                Left = work.Right - Width - 16;
                Top = work.Bottom - Height - 16;
            }
            ClampToWorkArea(work);
        }

        private void ClampToWorkArea()
        {
            try { ClampToWorkArea(SystemParameters.WorkArea); } catch { /* 工作区读取失败时保留原位 */ }
        }

        private void ClampToWorkArea(Rect work)
        {
            if (double.IsNaN(Left) || double.IsInfinity(Left)) Left = work.Right - Width - 16;
            if (double.IsNaN(Top) || double.IsInfinity(Top)) Top = work.Bottom - Height - 16;
            Left = Math.Min(Math.Max(Left, work.Left), Math.Max(work.Left, work.Right - Width));
            Top = Math.Min(Math.Max(Top, work.Top), Math.Max(work.Top, work.Bottom - Height));
        }

        private void SavePosition()
        {
            try
            {
                var db = DatabaseService.Instance;
                db.SetSetting("WidgetLeft", Left.ToString("R", CultureInfo.InvariantCulture));
                db.SetSetting("WidgetTop", Top.ToString("R", CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Widget save position error: {ex.Message}");
            }
        }

        #endregion

        #region 右键菜单

        private void MenuFull_Click(object sender, RoutedEventArgs e) => OpenFullMode();

        private void MenuSidebar_Click(object sender, RoutedEventArgs e)
        {
            try { App.SwitchDisplayMode(App.AppDisplayMode.Sidebar); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Widget open sidebar error: {ex.Message}"); }
        }

        private void MenuLock_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi) _locked = mi.IsChecked;
            try { DatabaseService.Instance.SetSetting("WidgetLocked", _locked ? "true" : "false"); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Widget save lock error: {ex.Message}"); }
            SyncMenuChecks();
        }

        private void MenuIdleFade_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi) _idleFade = mi.IsChecked;
            try { DatabaseService.Instance.SetSetting("WidgetIdleFade", _idleFade ? "true" : "false"); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Widget save idle fade error: {ex.Message}"); }
            SyncMenuChecks();
            if (!_idleFade) RestoreOpacity();
        }

        private void MenuSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settingsWindow = new SettingsWindow();
                settingsWindow.Owner = this;
                settingsWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Widget open settings error: {ex.Message}");
            }
        }

        private void MenuExit_Click(object sender, RoutedEventArgs e)
        {
            // 审查 P2：悬浮球 ShowInTaskbar=False 且不进 Alt+Tab，
            // 若启动形态即悬浮球，此前没有任何显式退出途径
            try { Application.Current.Shutdown(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Widget exit error: {ex.Message}"); }
        }

        #endregion
    }
}
