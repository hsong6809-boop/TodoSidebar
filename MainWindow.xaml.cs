using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TodoSidebar.Models;
using TodoSidebar.Services;
using TodoSidebar.ViewModels;
using TodoSidebar.Controls;

namespace TodoSidebar
{
    public partial class MainWindow : Window
    {
        private bool _isCollapsed = false;
        private const double ExpandedWidth = 352; // V2.3：与 MainWindow.xaml 的 Width/MainPanel 保持一致（曾为 320 导致首开右缘裁切）
        private const double CollapsedWidth = 3;
        private const int TriggerHitWidth = 30;      // 收起状态下触发条命中宽度(px)
        private const double MainScreenHeightRatio = 0.66;  // 主屏高度比例
        
        // 悬停延迟定时器
        private readonly DispatcherTimer _hoverDelayTimer;
        private const int HoverDelayMilliseconds = 250;
        
        // 收起延迟定时器
        private readonly DispatcherTimer _collapseDelayTimer;
        private const int CollapseDelayMilliseconds = 300;
        
        private readonly DispatcherTimer _dateTimeTimer;
        private readonly DispatcherTimer _mouseCheckTimer;
        // M31：展开兜底定时器（存字段便于重建前先停旧的，避免多个兜底并存）
        private DispatcherTimer? _failSafeTimer;
        private DateTime _lastCollapseTime = DateTime.MinValue;
        private const int CollapseCooldownMs = 500;

        // V2.4：置顶保持定时器
        private DispatcherTimer? _topmostTimer;
        
        // 当前展开的任务
        private FrameworkElement? _expandedTaskCard;
        // M32：记录展开卡片的任务 Id，集合重建后据此恢复展开状态
        private int? _expandedTaskId;
        
        // 防重入锁
        private bool _isAnimating = false;
        private DateTime _lastClickTime = DateTime.MinValue;
        private const int ClickCooldownMs = 300;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = App.SharedViewModel;

            // 订阅升级/成就事件：显示横幅 + 粒子特效
            if (DataContext is MainViewModel vm)
            {
                vm.LevelUpOccurred += OnLevelUpOccurred;
                vm.AchievementUnlockedOccurred += OnAchievementUnlockedOccurred;

                // M32：集合重建（LoadCurrentTasks 先 Clear 再逐个 Add）后按 Id 恢复卡片展开状态
                vm.CurrentTasks.CollectionChanged += OnCurrentTasksChanged;
            }

            // 订阅番茄钟事件：刷新迷你计时器
            PomodoroService.Instance.Tick += OnFocusTick;
            PomodoroService.Instance.StateChanged += OnFocusStateChanged;
            PomodoroService.Instance.SessionCompleted += OnFocusSessionCompleted;
            UpdateMiniFocus();

            // v5.2 账号中心：侧边栏头像随账号资料变化刷新
            AccountService.Instance.ProfileChanged += OnAccountProfileChanged;
            RefreshAccountAvatar();
            
            // 初始化悬停延迟定时器
            _hoverDelayTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(HoverDelayMilliseconds)
            };
            _hoverDelayTimer.Tick += HoverDelayTimer_Tick;
            
            // 初始化收起延迟定时器
            _collapseDelayTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(CollapseDelayMilliseconds)
            };
            _collapseDelayTimer.Tick += CollapseDelayTimer_Tick;
            
            // 鼠标检测定时器，只在需要时激活
            _mouseCheckTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };
            _mouseCheckTimer.Tick += MouseCheckTimer_Tick;

            // 触发条悬停 → 延迟展开
            TriggerStrip.MouseEnter += (_, _) =>
            {
                if ((DateTime.Now - _lastCollapseTime).TotalMilliseconds < CollapseCooldownMs)
                    return;
                if (_isCollapsed && !_hoverDelayTimer.IsEnabled)
                    _hoverDelayTimer.Start();
            };
            
            // 触发条点击 → 立即展开（兜底机制，防止悬停检测失败）
            TriggerStrip.MouseLeftButtonDown += (_, _) =>
            {
                if (_isCollapsed && !_isAnimating)
                {
                    _hoverDelayTimer.Stop();
                    ExpandPanel();
                }
            };
            
            // 窗口内鼠标移动 → 取消收起计时
            MouseMove += (_, _) =>
            {
                _collapseDelayTimer.Stop();
                // 如果已收起，开始检测光标是否靠近触发条
                if (_isCollapsed && !_mouseCheckTimer.IsEnabled)
                    _mouseCheckTimer.Start();
            };
            
            // 窗口失焦 → 立即收起
            Deactivated += (_, _) =>
            {
                if (_isCollapsed || _isAnimating) return;

                // M30：存在由本窗口拥有且处于活动状态的子窗口（设置/统计/详情等模态框）时
                // 跳过收起，避免打开对话框瞬间把面板收走
                bool hasActiveOwnedWindow = Application.Current.Windows
                    .OfType<Window>()
                    .Any(w => !ReferenceEquals(w, this) && w.Owner == this && w.IsActive);
                if (hasActiveOwnedWindow) return;

                _lastCollapseTime = DateTime.Now;
                CollapsePanel();
            };

            // 鼠标检测在首次收起后启动，初始展开状态不需要
            _dateTimeTimer = new DispatcherTimer
            {
                // L23 修复：间隔 30 秒 → 1 秒，与 HH:mm:ss 秒级显示匹配（DispatcherTimer 开销可忽略）
                Interval = TimeSpan.FromSeconds(1)
            };
            _dateTimeTimer.Tick += (s, args) =>
            {
                UpdateDateTime();
                UpdateGreeting();
                // L23 配套：顺带刷新可见任务的截止紧急程度文本，避免"3小时后"停滞
                RefreshVisibleDeadlineUrgency();
            };
            _dateTimeTimer.Start();
            UpdateDateTime(); // 立即更新一次
            
            // 窗口关闭时清理定时器（ViewModel 由 App.OnExit 统一销毁）
            Closing += (s, e) =>
            {
                _hoverDelayTimer.Stop();
                _collapseDelayTimer.Stop();
                _dateTimeTimer.Stop();
                _mouseCheckTimer.Stop();
                StopFailSafeTimer(); // M31：兜底定时器一并清理
                // R43 修复（审查 M1）：置顶保持计时器一并停止——原实现漏停，
                // Tick 闭包捕获 this 导致每次关闭（如切换完整模式）泄漏整个窗口可视树，
                // 且每 3 秒对已销毁 HWND 调一次无效 SetWindowPos
                _topmostTimer?.Stop();
            };
        }

        /// <summary>窗口关闭时退订单例/长生命周期事件，防止窗口无法被回收</summary>
        protected override void OnClosed(EventArgs e)
        {
            // 退订 ViewModel 事件（DataContext 判空）
            if (DataContext is MainViewModel vm)
            {
                vm.LevelUpOccurred -= OnLevelUpOccurred;
                vm.AchievementUnlockedOccurred -= OnAchievementUnlockedOccurred;
                vm.CurrentTasks.CollectionChanged -= OnCurrentTasksChanged;
            }

            // 退订番茄钟单例事件
            PomodoroService.Instance.Tick -= OnFocusTick;
            PomodoroService.Instance.StateChanged -= OnFocusStateChanged;
            PomodoroService.Instance.SessionCompleted -= OnFocusSessionCompleted;

            // 退订账号中心事件
            AccountService.Instance.ProfileChanged -= OnAccountProfileChanged;

            base.OnClosed(e);
        }

        /// <summary>v5.2 账号中心：头像资料变化时回 UI 线程刷新。</summary>
        private void OnAccountProfileChanged(object? sender, EventArgs e)
            => Dispatcher.Invoke(RefreshAccountAvatar);

        private void UpdateDateTime()
        {
            var now = DateTime.Now;
            var dayOfWeek = now.DayOfWeek switch
            {
                DayOfWeek.Monday => "周一",
                DayOfWeek.Tuesday => "周二",
                DayOfWeek.Wednesday => "周三",
                DayOfWeek.Thursday => "周四",
                DayOfWeek.Friday => "周五",
                DayOfWeek.Saturday => "周六",
                DayOfWeek.Sunday => "周日",
                _ => ""
            };
            DateTimeText.Text = $"{now:M月d日} {dayOfWeek} · {now:HH:mm}";
        }

        /// <summary>v5.2 账号中心：刷新侧边栏左上角头像。</summary>
        private void RefreshAccountAvatar()
        {
            if (SidebarAvatar == null) return;
            var fallback = !string.IsNullOrWhiteSpace(App.Nickname)
                ? App.Nickname
                : SafeEmailPrefix();
            AvatarLoader.Load(SidebarAvatar, AccountService.Instance, 42, fallback);
        }

        private static string SafeEmailPrefix()
        {
            try { return ((IAuthService)AuthService.Instance).CurrentEmail ?? ""; }
            catch { return ""; }
        }

        /// <summary>v5.2 账号中心：点击侧边栏头像打开账号中心。</summary>
        private void Avatar_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            e.Handled = true; // 阻断冒泡到头部拖拽区
            OpenAccountCenter();
        }

        private void OpenAccountCenter()
        {
            var win = new AccountWindow { Owner = this };
            win.Show();
            win.Activate();
        }

        /// <summary>V2：驾驶舱问候语（时段 + 用户名），时钟每秒刷新时顺带更新。</summary>
        private void UpdateGreeting()
        {
            if (GreetingText == null) return;
            var now = DateTime.Now;
            var part = now.Hour switch
            {
                < 6 => "凌晨好",
                < 12 => "早上好",
                < 14 => "中午好",
                < 18 => "下午好",
                _ => "晚上好"
            };
            string name;
            if (!string.IsNullOrWhiteSpace(App.Nickname))
            {
                name = App.Nickname;
            }
            else
            {
                name = "朋友";
                try
                {
                    var email = ((IAuthService)AuthService.Instance).CurrentEmail;
                    if (!string.IsNullOrWhiteSpace(email))
                        name = email.Split('@')[0];
                }
                catch { /* 未登录/服务异常时用默认称呼 */ }
            }
            GreetingText.Text = $"{part}，{name}";
        }

        /// <summary>V2：今日已完成分组折叠切换。</summary>
        private void CompletedHeader_Click(object sender, MouseButtonEventArgs e)
        {
            if (CompletedList == null || CompletedChevron == null) return;
            var opening = CompletedList.Visibility != Visibility.Visible;
            CompletedList.Visibility = opening ? Visibility.Visible : Visibility.Collapsed;
            CompletedChevron.Glyph = opening ? Icons.ChevronUp : Icons.ChevronDown;
        }

        #region 成就

        /// <summary>打开成就图鉴</summary>
        private void BrowseBadges_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var window = new AchievementWindow { Owner = this };
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BrowseBadges error: {ex.Message}");
            }
        }

        /// <summary>徽章解锁：横幅 + 粒子（复用升级横幅）</summary>
        private void OnAchievementUnlockedOccurred(object? sender, TodoSidebar.Models.AchievementUnlockedEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnAchievementUnlockedOccurred(sender, e)));
                return;
            }

            try
            {
                LevelUpTitleText.Text = $"🏅 成就解锁：{e.Name}";
                LevelUpSubText.Text = e.Description;
                LevelUpBanner.Visibility = Visibility.Visible;
                AnimationService.AnimateFadeIn(LevelUpBanner);

                var bannerCenter = new System.Windows.Point(
                    LevelUpBanner.ActualWidth > 0 ? LevelUpBanner.ActualWidth / 2 : 120,
                    LevelUpBanner.ActualHeight > 0 ? LevelUpBanner.ActualHeight / 2 : 60);
                AnimationService.CreateCompletionParticles(ParticleLayer, bannerCenter);

                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                timer.Tick += (s, args) =>
                {
                    timer.Stop();
                    AnimationService.AnimateFadeOut(LevelUpBanner, 300);
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Achievement banner error: {ex.Message}");
            }
        }

        #endregion

        #region 迷你番茄钟

        private void OnFocusTick(object? sender, EventArgs e) => UpdateMiniFocus();

        private void OnFocusStateChanged(object? sender, PomodoroState state) => UpdateMiniFocus();

        private void OnFocusSessionCompleted(object? sender, PomodoroSessionCompletedEventArgs e)
        {
            UpdateMiniFocus();
            if (!e.Completed)
            {
                System.Diagnostics.Debug.WriteLine("Pomodoro interrupted");
                return;
            }
            // 完成提示（复用通知窗口）
            var msg = e.TaskId.HasValue
                ? $"专注完成 +{(e.TaskId.HasValue ? 10 : 5)} XP"
                : "专注完成 +5 XP";
            if (e.EstimatedReached)
                msg += "\n🎯 已达预估专注时长，可以收尾啦！";
            try
            {
                NotificationService.Instance.ShowNotification("🍅 番茄完成", msg);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Pomodoro notify error: {ex.Message}");
            }
        }

        private void UpdateMiniFocus()
        {
            var pomo = PomodoroService.Instance;
            MiniFocusTimer.Text = PomodoroService.FormatTime(pomo.RemainingSeconds);
            var progress = pomo.TotalSeconds > 0
                ? 1.0 - (double)pomo.RemainingSeconds / pomo.TotalSeconds
                : 0;
            MiniFocusRing.Progress = Math.Clamp(progress, 0, 1);

            MiniFocusState.Text = pomo.State switch
            {
                PomodoroState.Focus => string.IsNullOrEmpty(pomo.BoundTaskTitle)
                    ? "🍅 专注中…"
                    : $"🍅 专注「{pomo.BoundTaskTitle}」",
                PomodoroState.Paused => "⏸ 已暂停",
                PomodoroState.Break => "☕ 休息中…",
                _ => $"🍅 未开始 · 今日已完成 {pomo.GetTodayStats().completed} 个"
            };
        }

        private void MiniFocusStart_Click(object sender, RoutedEventArgs e)
        {
            // 暂停状态点开始 = 继续
            if (PomodoroService.Instance.State == PomodoroState.Paused)
            {
                PomodoroService.Instance.Resume();
                return;
            }
            PomodoroService.Instance.Start();
        }

        private void MiniFocusPause_Click(object sender, RoutedEventArgs e)
        {
            if (PomodoroService.Instance.State == PomodoroState.Focus)
                PomodoroService.Instance.Pause();
            else if (PomodoroService.Instance.State == PomodoroState.Paused)
                PomodoroService.Instance.Resume();
        }

        private void MiniFocusStop_Click(object sender, RoutedEventArgs e)
        {
            if (PomodoroService.Instance.State is PomodoroState.Focus or PomodoroState.Paused)
            {
                // 确认中断（避免误点丢 XP）。
                // R45 修复（审查 L2）：设置 owner=this——Win32 MessageBox 不在 WPF 窗口树内，
                // 无 owner 时弹出瞬间主窗口 Deactivated 会触发失焦自动收起，确认框背后动画消失
                var result = MessageBox.Show(this, "停止当前番茄将视为中断，不获得经验。确定停止吗？",
                    "停止专注", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                    PomodoroService.Instance.Stop(complete: false);
            }
        }

        #endregion

        /// <summary>
        /// 升级反馈：横幅滑入显示 3 秒 + 粒子爆炸。
        /// </summary>
        private void OnLevelUpOccurred(object? sender, TodoSidebar.Models.LevelUpEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnLevelUpOccurred(sender, e)));
                return;
            }

            try
            {
                LevelUpTitleText.Text = $"🎉 升级！Lv.{e.NewLevel}";
                LevelUpSubText.Text = $"获得新称号「{e.NewTitle}」";
                LevelUpBanner.Visibility = Visibility.Visible;
                AnimationService.AnimateFadeIn(LevelUpBanner);

                // 粒子特效（位于横幅附近）
                var bannerCenter = new System.Windows.Point(
                    LevelUpBanner.ActualWidth > 0 ? LevelUpBanner.ActualWidth / 2 : 120,
                    LevelUpBanner.ActualHeight > 0 ? LevelUpBanner.ActualHeight / 2 : 60);
                for (int i = 0; i < 3; i++)
                {
                    AnimationService.CreateCompletionParticles(ParticleLayer, bannerCenter);
                }

                // 3 秒后淡出
                var timer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(3)
                };
                timer.Tick += (s, args) =>
                {
                    timer.Stop();
                    AnimationService.AnimateFadeOut(LevelUpBanner, 300);
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LevelUp banner error: {ex.Message}");
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var helper = new WindowInteropHelper(this);
                var screen = System.Windows.Forms.Screen.FromHandle(helper.Handle);
                var screenHeight = screen.Bounds.Height;
                var screenLeft = screen.Bounds.X;
                var screenTop = screen.Bounds.Y;

                // WinForms Screen.Bounds 是物理像素，WPF 属性使用 DIP，需按当前 DPI 换算
                var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);

                Width = ExpandedWidth; // 宽度保持 DIP 值
                Height = screenHeight / dpi.DpiScaleY * MainScreenHeightRatio;
                Left = screenLeft / dpi.DpiScaleX;
                Top = (screenTop + (screenHeight - Height * dpi.DpiScaleY) / 2) / dpi.DpiScaleY;

                // 置顶由 XAML 的 Topmost="True" 保证，无需再调用 SetWindowPos
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Window_Loaded position init failed, fallback: {ex.Message}");
                Left = 0;
                Top = 100;
                Width = ExpandedWidth;
                Height = 600;
            }

            // P2：真实亚克力背板（失败静默降级为半透明纯色）
            DwmBackdropHelper.ApplyMainShellAcrylic(this);

            // V2.4+：置顶保持——每 3 秒重申 HWND_TOPMOST，防止其他全屏/置顶应用抢占层级
            _topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _topmostTimer.Tick += (_, _) => ReAssertTopmost();
            _topmostTimer.Start();
            ReAssertTopmost();
            Deactivated += (_, _) => ReAssertTopmost();
        }

        #region 置顶保持

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HwndTopmost = new(-1);
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoActivate = 0x0010;

        /// <summary>
        /// V2.4：重申侧边栏永远置顶。WPF 的 Topmost 只保证进入 TOPMOST 层，
        /// 其他同样置顶的全屏应用（视频播放器/F11 浏览器）激活后会压到本窗口之上；
        /// 周期性以 SWP_NOACTIVATE 重设层级即可稳定压回，且不抢焦点。
        /// 注意：独占全屏（DirectX 排斥模式）受系统限制无法覆盖，属正常现象。
        /// </summary>
        private void ReAssertTopmost()
        {
            try
            {
                // R44 修复（审查 M1 附带缺口）：收起态不再跳过重申——3px 触发条在收起态仍然可见、
                // 是"贴边悬停展开"的唯一入口，若被其他置顶/全屏应用盖住，用户将永远无法唤出侧边栏。
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;
                SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0,
                    SwpNoMove | SwpNoSize | SwpNoActivate);
                if (!Topmost) Topmost = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ReAssertTopmost error: {ex.Message}");
            }
        }

        #endregion

        #region 鼠标悬停展开/收起

        private void CollapseDelayTimer_Tick(object? sender, EventArgs e)
        {
            _collapseDelayTimer.Stop();
            if (!_isCollapsed)
            {
                _lastCollapseTime = DateTime.Now;
                CollapsePanel();
            }
        }

        private void MouseCheckTimer_Tick(object? sender, EventArgs e)
        {
            if (_isAnimating) return;

            if (_isCollapsed)
            {
                // 收起状态：使用 Win32 API 获取像素级坐标
                GetCursorPos(out var cursorPos);
                var hwnd = new WindowInteropHelper(this).Handle;
                GetWindowRect(hwnd, out var windowRect);
                // R46 修复（审查 L4）：命中宽度按 DPI 缩放——TriggerHitWidth 是 DIP 设计值，
                // GetCursorPos/GetWindowRect 返回物理像素，150% 缩放下 30px 只剩约 20 DIP 手感
                double dpiScale = 1.0;
                try { dpiScale = System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX; }
                catch { /* 取不到 DPI 时按 100% 处理 */ }
                var hitWidthPx = (int)Math.Round(TriggerHitWidth * dpiScale);
                var triggerRight = windowRect.Left + hitWidthPx;

                if (cursorPos.X >= windowRect.Left && cursorPos.X <= triggerRight
                    && cursorPos.Y >= windowRect.Top && cursorPos.Y <= windowRect.Bottom)
                {
                    if ((DateTime.Now - _lastCollapseTime).TotalMilliseconds >= CollapseCooldownMs)
                    {
                        // 鼠标在触发区 → 启动悬停延迟（仅在未运行时启动，避免每150ms重置导致永远无法到期）
                        if (!_hoverDelayTimer.IsEnabled)
                        {
                            _hoverDelayTimer.Start();
                        }
                    }
                }
                else
                {
                    // 鼠标离开触发区 → 停止悬停延迟
                    _hoverDelayTimer.Stop();
                }
            }
            else
            {
                if (!IsMouseOver)
                {
                    _collapseDelayTimer.Start();
                }
            }
        }

        private void HoverDelayTimer_Tick(object? sender, EventArgs e)
        {
            _hoverDelayTimer.Stop();
            if (_isCollapsed)
            {
                ExpandPanel();
            }
        }

        #endregion

        #region 窗口操作

        private void CollapseButton_Click(object sender, RoutedEventArgs e)
        {
            _lastCollapseTime = DateTime.Now;
            CollapsePanel();
        }

        private void ExpandFullMode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var fullWindow = new FullWindow();
                fullWindow.Show();
                // M28：切换窗口后把全局热键迁移到新窗口，否则本窗口销毁后热键静默失效
                HotkeyService.Current?.ReRegisterHotkeys(fullWindow);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"切换模式失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                DragMove();
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Header drag error: {ex.Message}"); }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settingsWindow = new SettingsWindow();
                settingsWindow.Owner = this;
                settingsWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开设置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StatisticsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var statisticsWindow = new StatisticsWindow();
                statisticsWindow.Owner = this;
                statisticsWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开统计失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private async void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show("确定要退出登录吗？\n\n退出后需要重新输入账号密码登录。", 
                    "退出登录", MessageBoxButton.YesNo, MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    // R19 修复（审查 H3）：LogoutAsync 现在保证本地清理必定完成，
                    // 返回值仅表示服务端吊销是否成功——失败也要继续登出流程，但如实提示
                    var serverSignOutOk = await AuthService.Instance.LogoutAsync();

                    // 释放共享 ViewModel 并停止后台服务（通知/同步/定时器），避免登出后空转
                    App.StopBackgroundServices();
                    
                    // 关闭主窗口，显示登录窗口
                    var loginWindow = new LoginWindow();
                    loginWindow.Show();
                    Close();

                    if (!serverSignOutOk)
                    {
                        MessageBox.Show("本机已退出登录，但服务器会话注销失败（网络原因）。\n如需确保其他设备安全，请稍后重新登录或修改密码。",
                            "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"退出登录失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region 展开/收起动画

        private void CollapsePanel()
        {
            if (_isAnimating) return;
            _isCollapsed = true;
            _lastCollapseTime = DateTime.Now;
            _collapseDelayTimer.Stop();
            _mouseCheckTimer.Start(); // 收起后开始检测靠近
            AnimatePanel(false);
        }

        private void ExpandPanel()
        {
            if (_isAnimating) return;
            _isCollapsed = false;
            _hoverDelayTimer.Stop();
            _collapseDelayTimer.Stop(); // 展开时必须停止收起定时器，防止立即被收回
            // 注意：不停止 _mouseCheckTimer，保持运行以检测鼠标离开窗口

            // M31：先停掉上一次的兜底定时器，避免快速收起/展开时多个兜底并存
            StopFailSafeTimer();

            // 安全检查：如果 MainPanel 宽度已经正确但不可见，强制恢复
            if (MainPanel.Width >= ExpandedWidth - 1 && MainPanel.Opacity < 0.1)
            {
                MainPanel.Opacity = 1;
            }

            AnimatePanel(true);

            // 兜底机制：1 秒后如果还没展开，强制恢复可见
            _failSafeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1000)
            };
            _failSafeTimer.Tick += (s, e) =>
            {
                StopFailSafeTimer();

                // M31：若期间面板已被快速收起，直接退出，避免与收起状态竞争产生幽灵面板
                if (_isCollapsed) return;

                if (MainPanel.Opacity < 0.5 || MainPanel.Width < ExpandedWidth - 10)
                {
                    MainPanel.BeginAnimation(UIElement.OpacityProperty, null);
                    MainPanel.Opacity = 1;
                    MainPanel.Width = ExpandedWidth;
                    Width = ExpandedWidth;
                    _isAnimating = false;
                }
            };
            _failSafeTimer.Start();
        }

        /// <summary>M31：停止并清空展开兜底定时器</summary>
        private void StopFailSafeTimer()
        {
            _failSafeTimer?.Stop();
            _failSafeTimer = null;
        }

        private void AnimatePanel(bool expand)
        {
            if (_isAnimating) return;
            _isAnimating = true;

            try
            {
                // 清除旧动画
                MainPanel.BeginAnimation(UIElement.OpacityProperty, null);
                BeginAnimation(WidthProperty, null);

                var duration = TimeSpan.FromMilliseconds(450);
                var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

                if (expand)
                {
                    // 展开：先设宽度再淡入，避免布局抖动
                    Width = ExpandedWidth;
                    MainPanel.Width = ExpandedWidth;
                    MainPanel.Opacity = 0;

                    var fadeIn = new DoubleAnimation(1, duration) { EasingFunction = easing };
                    fadeIn.Completed += (s, e) =>
                    {
                        MainPanel.BeginAnimation(UIElement.OpacityProperty, null);
                        MainPanel.Opacity = 1;  // 修复：动画清除后局部值会回退到0，必须显式设为1
                        _isAnimating = false;
                    };
                    MainPanel.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                }
                else
                {
                    // 收起：先淡出再设宽度
                    var fadeOut = new DoubleAnimation(0, duration) { EasingFunction = easing };
                    fadeOut.Completed += (s, e) =>
                    {
                        MainPanel.BeginAnimation(UIElement.OpacityProperty, null);
                        MainPanel.Opacity = 0;  // 显式同步局部值，保持一致性
                        MainPanel.Width = 0;
                        Width = CollapsedWidth;
                        _isAnimating = false;
                    };
                    MainPanel.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AnimatePanel error: {ex.Message}");
                _isAnimating = false;
            }
        }

        #endregion

        #region 任务操作

        // V2.5+：侧边栏快速添加已由「接下来」行动卡取代（新建走完整模式 Composer）

        private void TaskCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (!CheckClickCooldown()) return;
            
            try
            {
                if (sender is FrameworkElement element)
                {
                    var expandArea = FindChild<System.Windows.Controls.StackPanel>(element, "ExpandArea");
                    if (expandArea != null)
                    {
                        if (expandArea.Visibility == Visibility.Visible)
                        {
                            expandArea.Visibility = Visibility.Collapsed;
                            _expandedTaskCard = null;
                            _expandedTaskId = null; // M32：折叠时同步清除记录
                        }
                        else
                        {
                            if (_expandedTaskCard != null)
                            {
                                var prevExpandArea = FindChild<System.Windows.Controls.StackPanel>(_expandedTaskCard, "ExpandArea");
                                if (prevExpandArea != null)
                                {
                                    prevExpandArea.Visibility = Visibility.Collapsed;
                                }
                            }
                            
                            expandArea.Visibility = Visibility.Visible;
                            _expandedTaskCard = element;
                            // M32：记录任务 Id，集合重建（LoadCurrentTasks）后可按 Id 恢复展开
                            _expandedTaskId = (element.DataContext as TaskItem)?.Id;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TaskCard_Click error: {ex.Message}");
            }
        }

        private void SubTaskInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                try
                {
                    if (sender is TextBox textBox)
                    {
                        // 获取父级 TaskItem
                        var parent = System.Windows.Media.VisualTreeHelper.GetParent(textBox);
                        while (parent != null)
                        {
                            if (parent is FrameworkElement fe && fe.DataContext is TaskItem task)
                            {
                                if (DataContext is MainViewModel vm && !string.IsNullOrWhiteSpace(vm.NewSubTaskTitle))
                                {
                                    vm.AddSubTaskCommand.Execute(task);
                                }
                                break;
                            }
                            parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SubTaskInput_KeyDown error: {ex.Message}");
                }
            }
        }

        private void SubTaskCheckBox_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is CheckBox checkBox && checkBox.DataContext is SubTask subTask)
                {
                    var parent = System.Windows.Media.VisualTreeHelper.GetParent(checkBox);
                    while (parent != null)
                    {
                        if (parent is ListBoxItem listBoxItem && listBoxItem.DataContext is TaskItem task)
                        {
                            if (DataContext is MainViewModel vm)
                            {
                                vm.ToggleSubTaskCommand.Execute(new object[] { task, subTask });
                            }
                            break;
                        }
                        parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SubTaskCheckBox_Click error: {ex.Message}");
            }
        }

        /// <summary>
        /// M32：CurrentTasks 集合变化处理。LoadCurrentTasks 重建集合（Clear 触发 Reset）
        /// 会销毁旧的可视容器，展开状态需按 Id 在新容器上恢复。
        /// </summary>
        private void OnCurrentTasksChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // 仅关心重建动作；逐个 Add 时容器尚未生成，延迟到布局完成后再恢复
            if (e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Reset) return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                RestoreExpandedCard();
                // L23 配套：LoadCurrentTasks 重建集合后立即按当前时间刷新紧急程度文本
                RefreshVisibleDeadlineUrgency();
            }), DispatcherPriority.Background);
        }

        /// <summary>
        /// L23 配套：对当前可见任务调用 RefreshDeadlineUrgency（L13 新增），
        /// 让"3小时后"等文本按当前时间重算；配合 1 秒定时器保持实时。
        /// </summary>
        private void RefreshVisibleDeadlineUrgency()
        {
            try
            {
                foreach (var item in TaskListBox.Items)
                {
                    if (item is TaskItem task)
                        task.RefreshDeadlineUrgency();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RefreshVisibleDeadlineUrgency error: {ex.Message}");
            }
        }

        /// <summary>
        /// M32：遍历 ItemContainerGenerator 找到 Id 匹配的新容器，重新显示其 ExpandArea。
        /// 任务已不存在（被删除/完成）时清除记录，避免悬空引用与串卡。
        /// </summary>
        private void RestoreExpandedCard()
        {
            try
            {
                if (_expandedTaskId == null) return;

                foreach (var item in TaskListBox.Items)
                {
                    if (item is TaskItem task && task.Id == _expandedTaskId)
                    {
                        if (TaskListBox.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement container)
                        {
                            var expandArea = FindChild<System.Windows.Controls.StackPanel>(container, "ExpandArea");
                            if (expandArea != null)
                            {
                                expandArea.Visibility = Visibility.Visible;
                                // 同步更新可视元素引用，保证再次点击时能正确折叠旧卡
                                var cardBorder = FindChild<System.Windows.Controls.Border>(container, "TaskCard");
                                if (cardBorder != null)
                                    _expandedTaskCard = cardBorder;
                            }
                        }
                        return;
                    }
                }

                // 列表中已找不到该任务，清除记录
                _expandedTaskId = null;
                _expandedTaskCard = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RestoreExpandedCard error: {ex.Message}");
            }
        }

        #endregion

        #region 拖拽排序

        private Point _dragStartPoint;
        private bool _isDragging;
        private TaskItem? _draggedTask;

        private void TaskListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            _isDragging = false;

            var listBoxItem = FindParent<ListBoxItem>((DependencyObject)e.OriginalSource);
            if (listBoxItem?.DataContext is TaskItem task)
            {
                _draggedTask = task;
            }
            else
            {
                // M25：未命中任务卡片时清空残留引用，避免误拖上一条任务
                _draggedTask = null;
            }
        }

        private void TaskListBox_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _isDragging)
                return;

            var pos = e.GetPosition(null);
            var diff = _dragStartPoint - pos;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                if (_draggedTask != null)
                {
                    _isDragging = true;
                    var data = new DataObject("TaskItem", _draggedTask);
                    try
                    {
                        DragDrop.DoDragDrop(TaskListBox, data, DragDropEffects.Move);
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"DragDrop error: {ex.Message}"); }
                    finally
                    {
                        // M25：无论正常结束、ESC 取消还是在列表外释放鼠标，都必须复位拖拽状态，
                        // 否则 _isDragging 永远为 true，后续拖拽全部失灵
                        _isDragging = false;
                        _draggedTask = null;
                    }
                }
            }
        }

        private void TaskListBox_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void TaskListBox_Drop(object sender, DragEventArgs e)
        {
            try
            {
                if (e.Data.GetData("TaskItem") is TaskItem draggedTask)
                {
                    var listBoxItem = FindParent<ListBoxItem>((DependencyObject)e.OriginalSource);
                    if (listBoxItem?.DataContext is TaskItem targetTask && draggedTask != targetTask)
                    {
                        if (DataContext is MainViewModel vm)
                        {
                            vm.ReorderTasksCommand.Execute(new object[] { draggedTask, targetTask });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TaskListBox_Drop error: {ex.Message}");
            }
            _isDragging = false;
            _draggedTask = null;
        }

        #endregion

        #region 搜索

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.IsSearchMode = !vm.IsSearchMode;
                    if (vm.IsSearchMode)
                    {
                        Dispatcher.BeginInvoke(new Action(() => SearchInput.Focus()), DispatcherPriority.Background);
                    }
                    else
                    {
                        vm.ClearSearchCommand.Execute(null);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SearchButton_Click error: {ex.Message}");
            }
        }

        private void SearchInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                try
                {
                    if (DataContext is MainViewModel vm)
                    {
                        vm.SearchCommand.Execute(null);
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"SearchInput_KeyDown error: {ex.Message}"); }
            }
        }

        private void TaskListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (sender is ListBox)
                {
                    var listBoxItem = FindParent<ListBoxItem>((DependencyObject)e.OriginalSource);
                    if (listBoxItem?.DataContext is TaskItem task && DataContext is MainViewModel vm)
                    {
                        var dialog = new TaskDetailDialog(task, vm);
                        dialog.Owner = this;
                        dialog.ShowDialog();

                        // M34 修复：任务编辑保存后清除已通知记录，否则修改截止日期后提醒永不恢复
                        if (dialog.DialogResult == true && !task.IsCompleted)
                        {
                            Services.NotificationService.Instance.ClearNotifiedTask(task.Id);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TaskListBox_MouseDoubleClick error: {ex.Message}");
            }
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 检查点击冷却时间，防止过快点击导致崩溃
        /// </summary>
        private bool CheckClickCooldown()
        {
            var now = DateTime.Now;
            if ((now - _lastClickTime).TotalMilliseconds < ClickCooldownMs)
            {
                return false;
            }
            _lastClickTime = now;
            return true;
        }

        private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            try
            {
                var parent = System.Windows.Media.VisualTreeHelper.GetParent(child);
                while (parent != null)
                {
                    if (parent is T typedParent)
                        return typedParent;
                    parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"FindParent error: {ex.Message}"); }
            return null;
        }

        private static T? FindChild<T>(DependencyObject parent, string childName) where T : DependencyObject
        {
            if (parent == null) return null;

            try
            {
                int childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
                for (int i = 0; i < childCount; i++)
                {
                    var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                    
                    if (child is T typedChild)
                    {
                        if (child is FrameworkElement fe && fe.Name == childName)
                        {
                            return typedChild;
                        }
                    }

                    var result = FindChild<T>(child, childName);
                    if (result != null) return result;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FindChild error: {ex.Message}");
            }

            return null;
        }

        #endregion

        // Win32 API
        // R47 修复（审查 L5）：删除未被调用的 int 版 SetWindowPos P/Invoke 及其常量——
        // 与上方 IntPtr 版并存属易误用死代码，后续维护者易拿 int 版配 SWP_SHOWWINDOW 造出抢焦点闪烁
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    }
}
