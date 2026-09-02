using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TodoSidebar.Models;
using TodoSidebar.Services;
using TodoSidebar.ViewModels;
using TodoSidebar.Controls;

namespace TodoSidebar
{
    public partial class FullWindow : Window
    {
        private TaskPriority _selectedPriority = TaskPriority.Medium;

        // 拖拽排序相关
        private Point _dragStartPoint;
        private bool _isDragging;
        private TaskItem? _draggedTask;

        // 今日统计缓存（M35）：番茄钟每秒 Tick 都会触发 UpdateFocusPanel，
        // 缓存后统计文本仅在过期/事件时查库，避免每秒 2 次 GetTodayStats() 的 SQLite 查询
        private (int completed, int interrupted, int minutes) _cachedTodayStats;
        private DateTime _statsLastRefreshed;

        public FullWindow()
        {
            InitializeComponent();
            DataContext = App.SharedViewModel;

            // P2：真实亚克力背板（失败静默降级为半透明纯色）+ V2.1 顶栏个性化
            Loaded += (_, _) =>
            {
                DwmBackdropHelper.ApplyMainShellAcrylic(this);
                UpdateThemeToggleGlyph();
                LoadDashboardHeader();
                UpdateSyncStatusUi();
            };

            // R(review 修复 v5.6)：同步状态实时刷新（后台线程触发时经 Dispatcher 调度到 UI 线程）
            SyncService.Instance.StatusChanged += OnSyncStatusChanged;

            // v5.2 账号中心：顶栏头像随账号资料变化刷新
            AccountService.Instance.ProfileChanged += OnAccountProfileChanged;
            RefreshAccountAvatar();

            // 订阅升级/成就事件：显示横幅 + 粒子特效
            if (DataContext is MainViewModel vm)
            {
                vm.LevelUpOccurred += OnLevelUpOccurred;
                vm.AchievementUnlockedOccurred += OnAchievementUnlockedOccurred;
            }

            // 订阅番茄钟事件：刷新专注页
            PomodoroService.Instance.Tick += OnFocusTick;
            PomodoroService.Instance.StateChanged += OnFocusStateChanged;
            PomodoroService.Instance.SessionCompleted += OnFocusSessionCompleted;
            LoadFocusTasks();
            UpdateFocusPanel();

            // 订阅每日挑战更新：刷新挑战面板
            DailyChallengeService.Instance.ChallengesUpdated += OnChallengesUpdated;
            LoadChallenges();
        }

        /// <summary>窗口关闭时退订单例/长生命周期事件，防止窗口无法被回收</summary>
        protected override void OnClosed(EventArgs e)
        {
            // R(review 修复 v5.6)：退订同步状态事件
            SyncService.Instance.StatusChanged -= OnSyncStatusChanged;

            // 退订 ViewModel 事件（DataContext 判空）
            if (DataContext is MainViewModel vm)
            {
                vm.LevelUpOccurred -= OnLevelUpOccurred;
                vm.AchievementUnlockedOccurred -= OnAchievementUnlockedOccurred;
            }

            // 退订番茄钟单例事件
            PomodoroService.Instance.Tick -= OnFocusTick;
            PomodoroService.Instance.StateChanged -= OnFocusStateChanged;
            PomodoroService.Instance.SessionCompleted -= OnFocusSessionCompleted;

            // 退订每日挑战单例事件
            DailyChallengeService.Instance.ChallengesUpdated -= OnChallengesUpdated;

            // 退订账号中心事件
            AccountService.Instance.ProfileChanged -= OnAccountProfileChanged;

            base.OnClosed(e);
        }

        /// <summary>v5.2 账号中心：头像资料变化时回 UI 线程刷新。</summary>
        private void OnAccountProfileChanged(object? sender, EventArgs e)
            => Dispatcher.Invoke(RefreshAccountAvatar);

        /// <summary>v5.2 账号中心：刷新顶栏头像。</summary>
        private void RefreshAccountAvatar()
        {
            if (TopBarAvatar == null) return;
            var fallback = !string.IsNullOrWhiteSpace(App.Nickname)
                ? App.Nickname
                : SafeEmailPrefix();
            AvatarLoader.Load(TopBarAvatar, AccountService.Instance, 28, fallback);
        }

        private static string SafeEmailPrefix()
        {
            try { return ((IAuthService)AuthService.Instance).CurrentEmail ?? ""; }
            catch { return ""; }
        }

        /// <summary>v5.2 账号中心：点击顶栏头像打开账号中心。</summary>
        private void Avatar_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            e.Handled = true;
            var win = new AccountWindow { Owner = this };
            win.Show();
            win.Activate();
        }

        #region 每日挑战面板

        private void OnChallengesUpdated(object? sender, EventArgs e) => LoadChallenges();

        /// <summary>加载今日挑战到面板</summary>
        private void LoadChallenges()
        {
            try
            {
                var challenges = DailyChallengeService.Instance.GetTodayChallenges();
                var items = challenges
                    .Select(c => new ChallengeItem(c))
                    .ToList();
                ChallengeList.ItemsSource = items;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadChallenges error: {ex.Message}");
            }
        }

        private class ChallengeItem
        {
            public string Icon { get; }
            public string Title { get; }
            public string ProgressText { get; }
            public string StatusText { get; }
            /// <summary>V2-W4：完成比例（0~1），供进度条宽度绑定。</summary>
            public double ProgressFraction { get; }
            /// <summary>V2 精修：进度条像素宽度（右栏卡片内轨道约 105px）。</summary>
            public double BarWidth { get; }

            public ChallengeItem(TodoSidebar.Models.DailyChallenge c)
            {
                Icon = c.Icon;
                Title = c.Title;
                ProgressText = $"{c.Progress}/{c.Target}";
                StatusText = c.Completed ? "✓" : "";
                ProgressFraction = Math.Clamp(c.Target > 0 ? (double)c.Progress / c.Target : 0, 0, 1);
                BarWidth = Math.Round(ProgressFraction * 105);
            }
        }

        #endregion

        #region 成就

        /// <summary>打开成就图鉴（V2-W7：改为导航到应用内成就页）。</summary>
        private void BrowseBadges_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                NavigateTo("Achievements");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BrowseBadges error: {ex.Message}");
            }
        }

        /// <summary>V2-W7：加载成就徽章墙。</summary>
        private void LoadAchievements()
        {
            try
            {
                var defs = AchievementService.Instance.GetDefinitions();
                var unlocked = DatabaseService.Instance.GetUnlockedAchievements();

                var items = defs.Select(d => new
                {
                    d.Icon,
                    d.Name,
                    d.Description,
                    Unlocked = unlocked.Contains(d.Id)
                }).ToList();

                AchievementsList.ItemsSource = items;
                AchievementsCountText.Text = $"{items.Count(i => i.Unlocked)} / {items.Count} 已解锁";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadAchievements error: {ex.Message}");
            }
        }

        /// <summary>V2-W7：打开 Ctrl+K 命令面板。</summary>
        private void OpenCommandPalette()
        {
            bool dark = ThemeManager.IsCurrentlyDark();
            var commands = new List<Controls.PaletteCommand>
            {
                new("前往 · 今日", "仪表盘与任务流", "Calendar", () => NavigateTo("Dashboard")),
                new("前往 · 截止", "按紧急度分组的截止任务", "Clock", () => NavigateTo("Deadline")),
                new("前往 · 历史", "已完成任务记录", "CheckList", () => NavigateTo("History")),
                new("前往 · 统计", "趋势图表与数据概览", "Chart", () => NavigateTo("Stats")),
                new("前往 · 专注", "沉浸式番茄钟", "Timer", () => NavigateTo("Focus")),
                new("前往 · 成就", "徽章图鉴", "Star", () => NavigateTo("Achievements")),
                new("新建任务", "聚焦到底部输入框", "Add",
                    () => { NavigateTo("Dashboard"); TaskInput.Focus(); }),
                new(dark ? "切换到浅色主题" : "切换到深色主题", "即时全局生效", dark ? "Eye" : "Lock",
                    () => ThemeManager.Instance.CurrentTheme = dark ? ThemeType.Light : ThemeType.Dark),
                new("上传到云端", "同步本机变更", "Upload",
                    () => (DataContext as MainViewModel)?.SyncViewModel?.UploadCommand?.Execute(null)),
                new("从云端下载", "拉取远端变更", "Download",
                    () => (DataContext as MainViewModel)?.SyncViewModel?.DownloadCommand?.Execute(null)),
                new("打开设置", "主题 / 强调色 / 数据管理", "Settings",
                    () => SettingsButton_Click(null!, new RoutedEventArgs())),
            };

            Controls.CommandPalette.Show(this, commands);
        }

        private void FullWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
            {
                OpenCommandPalette();
                e.Handled = true;
            }
        }

        /// <summary>顶栏搜索胶囊：打开命令面板。</summary>
        private void SearchCapsule_Click(object sender, RoutedEventArgs e) => OpenCommandPalette();

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
                    LevelUpBanner.ActualWidth > 0 ? LevelUpBanner.ActualWidth / 2 : 150,
                    LevelUpBanner.ActualHeight > 0 ? LevelUpBanner.ActualHeight / 2 : 60);
                AnimationService.CreateCompletionParticles(ParticleLayer, bannerCenter);

                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
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

        #region 专注页（番茄钟）

        private void LoadFocusTasks()
        {
            var tasks = (DataContext as MainViewModel)?.CurrentTasks;
            if (tasks == null) return;

            FocusTaskCombo.Items.Clear();
            FocusTaskCombo.Items.Add(new FocusTaskOption(0, "（不绑定任务）"));
            foreach (var task in tasks)
            {
                FocusTaskCombo.Items.Add(new FocusTaskOption(task.Id, task.Title));
            }
            FocusTaskCombo.SelectedIndex = 0;
        }

        private void OnFocusTick(object? sender, EventArgs e) => UpdateFocusPanel();

        private void OnFocusStateChanged(object? sender, PomodoroState state)
        {
            // 状态切换（开始/暂停/结束）会改变今日统计，先失效缓存再刷新
            InvalidateTodayStatsCache();
            UpdateFocusPanel();
        }

        private void OnFocusSessionCompleted(object? sender, PomodoroSessionCompletedEventArgs e)
        {
            // 会话完成（含中断）会改变今日统计，先失效缓存再刷新
            InvalidateTodayStatsCache();
            UpdateFocusPanel();

            // v5.6：会话结束自动淡出白噪音
            if (SoundService.Instance.IsPlaying)
                SoundService.Instance.Stop();

            if (!e.Completed) return;

            var msg = e.TaskId.HasValue ? "专注完成 +10 XP" : "专注完成 +5 XP";
            if (e.EstimatedReached)
                msg += "\n🎯 已达预估专注时长，可以收尾啦！";
            try
            {
                NotificationService.Instance.ShowNotification("🍅 番茄完成", msg);
                if (DataContext is MainViewModel vm)
                {
                    vm.LoadData(); // 刷新任务实际用时
                    LoadFocusTasks();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Pomodoro notify error: {ex.Message}");
            }
        }

        private void UpdateFocusPanel()
        {
            var pomo = PomodoroService.Instance;

            FocusTimerText.Text = PomodoroService.FormatTime(pomo.RemainingSeconds);
            var progress = pomo.TotalSeconds > 0
                ? 1.0 - (double)pomo.RemainingSeconds / pomo.TotalSeconds
                : 0;
            FocusRing.Progress = Math.Clamp(progress, 0, 1);

            FocusStateText.Text = pomo.State switch
            {
                PomodoroState.Focus => string.IsNullOrEmpty(pomo.BoundTaskTitle)
                    ? "🍅 专注中…"
                    : $"🍅 专注「{pomo.BoundTaskTitle}」",
                PomodoroState.Paused => "⏸ 已暂停",
                PomodoroState.Break => "☕ 休息中…",
                _ => "🍅 准备开始专注"
            };

            if (FocusStartLabel != null)
                FocusStartLabel.Text = pomo.State == PomodoroState.Paused ? "继续" : "开始专注";

            // 今日统计读缓存（M35）：Tick 每秒触发本方法，统计文本仅每 30 秒
            // （或状态变化/会话完成事件强制失效后）重新查库一次
            RefreshTodayStatsCache();
            var (completed, interrupted, minutes) = _cachedTodayStats;

            // V2-W6：沉浸式底部统计条
            if (StatPomodoros != null) StatPomodoros.Text = completed.ToString();
            if (StatMinutes != null) StatMinutes.Text = $"{minutes} 分";
            if (StatInterrupted != null) StatInterrupted.Text = interrupted.ToString();
            if (StatRounds != null) StatRounds.Text = $"{completed % PomodoroService.RoundsPerCycle}/{PomodoroService.RoundsPerCycle}";
            RefreshSessionDots(completed);

            // V2-W4：仪表盘快速专注卡同步刷新
            if (QuickFocusRing != null)
                QuickFocusRing.Progress = Math.Clamp(progress, 0, 1);
            if (QuickFocusTimeText != null)
                QuickFocusTimeText.Text = PomodoroService.FormatTime(pomo.RemainingSeconds);
            if (QuickFocusStateText != null)
                QuickFocusStateText.Text = pomo.State switch
                {
                    PomodoroState.Focus => "专注中…",
                    PomodoroState.Paused => "已暂停",
                    PomodoroState.Break => "休息中…",
                    _ => "准备开始"
                };
            if (QuickFocusButton != null && QuickFocusButton.Content is StackPanel sp && sp.Children.Count > 1 && sp.Children[1] is TextBlock label)
                label.Text = pomo.State == PomodoroState.Paused ? "继续" : "开始专注";
            if (QuickFocusRoundText != null)
                QuickFocusRoundText.Text = $"回合 {completed % PomodoroService.RoundsPerCycle} / {PomodoroService.RoundsPerCycle}";
        }

        /// <summary>缓存超过 30 秒未更新则重新查询今日统计，否则直接复用</summary>
        private void RefreshTodayStatsCache()
        {
            if ((DateTime.Now - _statsLastRefreshed).TotalSeconds < 30) return;
            _cachedTodayStats = PomodoroService.Instance.GetTodayStats();
            _statsLastRefreshed = DateTime.Now;
        }

        /// <summary>强制下次 UpdateFocusPanel 重新查询今日统计</summary>
        private void InvalidateTodayStatsCache() => _statsLastRefreshed = DateTime.MinValue;

        /// <summary>V2-W6：刷新专注回合圆点（已完成的填充主色渐变，未完成空心）。</summary>
        private void RefreshSessionDots(int completedToday)
        {
            if (SessionDots == null) return;
            var rounds = PomodoroService.RoundsPerCycle;
            var current = completedToday % rounds;

            if (SessionDots.Children.Count != rounds)
            {
                SessionDots.Children.Clear();
                for (int i = 0; i < rounds; i++)
                    SessionDots.Children.Add(new System.Windows.Shapes.Ellipse { Width = 9, Height = 9 });
            }

            var gradient = TryFindResource("AccentGradientBrush") as Brush;
            var outline = new SolidColorBrush(Color.FromArgb(0xFF, 0x33, 0x41, 0x5E)); outline.Freeze();

            for (int i = 0; i < SessionDots.Children.Count; i++)
            {
                if (SessionDots.Children[i] is not System.Windows.Shapes.Ellipse dot) continue;
                dot.Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 0);
                if (i < current && gradient != null)
                {
                    dot.Fill = gradient;
                    dot.StrokeThickness = 0;
                }
                else
                {
                    dot.Fill = Brushes.Transparent;
                    dot.Stroke = outline;
                    dot.StrokeThickness = 1.6;
                }
            }
        }

        private void FocusStart_Click(object sender, RoutedEventArgs e)
        {
            var pomo = PomodoroService.Instance;
            if (pomo.State == PomodoroState.Paused)
            {
                pomo.Resume();
                return;
            }
            if (pomo.State is PomodoroState.Focus or PomodoroState.Break)
                return;

            var option = FocusTaskCombo.SelectedItem as FocusTaskOption;
            var taskId = option != null && option.TaskId > 0 ? option.TaskId : (int?)null;
            var title = option != null && option.TaskId > 0 ? option.Display : "";
            pomo.Start(taskId, title);
        }

        private void FocusPause_Click(object sender, RoutedEventArgs e)
        {
            var pomo = PomodoroService.Instance;
            if (pomo.State == PomodoroState.Focus) pomo.Pause();
            else if (pomo.State == PomodoroState.Paused) pomo.Resume();
        }

        private void FocusStop_Click(object sender, RoutedEventArgs e)
        {
            var pomo = PomodoroService.Instance;
            if (pomo.State is not (PomodoroState.Focus or PomodoroState.Paused)) return;

            // R45 修复（审查 L2）：确认框设置 owner
            var result = MessageBox.Show(this, "停止当前番茄将视为中断，不获得经验。确定停止吗？",
                "停止专注", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
                pomo.Stop(complete: false);
        }

        /// <summary>专注页任务下拉选项</summary>
        private class FocusTaskOption
        {
            public int TaskId { get; }
            public string Display { get; }

            public FocusTaskOption(int taskId, string display)
            {
                TaskId = taskId;
                Display = display;
            }

            public string Title => Display; // ComboBox DisplayMemberPath
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
                    LevelUpBanner.ActualWidth > 0 ? LevelUpBanner.ActualWidth / 2 : 150,
                    LevelUpBanner.ActualHeight > 0 ? LevelUpBanner.ActualHeight / 2 : 60);
                for (int i = 0; i < 3; i++)
                {
                    AnimationService.CreateCompletionParticles(ParticleLayer, bannerCenter);
                }

                // 3 秒后淡出
                var timer = new System.Windows.Threading.DispatcherTimer
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

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                DragMove();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Header drag error: {ex.Message}");
            }
        }

        private void CollapseToSidebar_Click(object sender, RoutedEventArgs e)
        {
            // 关闭完整窗口，打开侧边栏窗口
            var sidebarWindow = new MainWindow();
            sidebarWindow.Show();
            // M28 修复：热键注册绑定在窗口句柄上，旧窗口销毁会自动注销全部热键，
            // 切换后必须重注册到新窗口，否则 Ctrl+Alt+T 等全局热键静默失效
            Services.HotkeyService.Current?.ReRegisterHotkeys(sidebarWindow);
            this.Close();
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
                MessageBox.Show($"打开设置窗口失败: {ex.Message}\n\n{ex.StackTrace}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    // R19 修复（审查 H3）：本地清理与服务端解耦，失败也继续登出并如实提示
                    var serverSignOutOk = await AuthService.Instance.LogoutAsync();

                    // 释放共享 ViewModel 并停止后台服务，避免登出后空转
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

        private void AddTaskButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                // V5.1：先做自然语言解析，再决定类型与补全字段
                var parsed = vm.ParseComposerInput();

                // 解析出了时间 → 自动归「截止」并同步分段（日期选择器会随 TabDeadline 可见）
                if (TabDaily.IsChecked == true && parsed.HasDue)
                    TabDeadline.IsChecked = true;
                if (parsed.HasDue)
                    vm.NewTaskDeadline = parsed.DueDate;

                // 优先级：解析关键词优先于手动选择；无关键词维持原手动选择
                if (parsed.Priority.HasValue)
                    SetPriorityRadio(parsed.Priority.Value);
                else
                    vm.NewTaskPriority = _selectedPriority;

                if (TabDeadline.IsChecked == true)
                {
                    vm.AddDeadlineTaskCommand.Execute(null);
                    AnimateLastItem(DeadlineTasksListBox);
                }
                else
                {
                    vm.AddDailyTaskCommand.Execute(null);
                    AnimateLastItem(DailyTasksListBox);
                }
            }
        }

        /// <summary>V5.1：回车即添加（与「添加」按钮同路径，含自然语言解析）。</summary>
        private void TaskInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AddTaskButton_Click(sender, e);
                e.Handled = true;
            }
        }

        /// <summary>同步优先级单选钮到指定值（解析关键词覆盖时调用）。</summary>
        private void SetPriorityRadio(TaskPriority priority)
        {
            var tag = priority switch
            {
                TaskPriority.High => "High",
                TaskPriority.Low => "Low",
                _ => "Medium"
            };
            foreach (var rb in new[] { PriorityHighRadio, PriorityMediumRadio, PriorityLowRadio })
                rb.IsChecked = rb.Tag as string == tag;
        }

        private void AnimateLastItem(ListBox listBox)
        {
            // L19 修复：新增任务后立即取容器时 ItemContainerGenerator 尚未生成容器，
            // ContainerFromItem 返回 null 导致动画基本不生效；延迟到 Loaded 优先级
            //（布局完成、容器已生成）后再执行原动画逻辑
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                try
                {
                    if (listBox.Items.Count > 0)
                    {
                        var lastItem = listBox.Items[listBox.Items.Count - 1];
                        // L19 修复：取容器前判空保护，极端情况下容器仍可能未生成
                        var container = listBox.ItemContainerGenerator.ContainerFromItem(lastItem) as FrameworkElement;
                        if (container != null)
                        {
                            AnimationService.AnimateAdd(container);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"AnimateLastItem error: {ex.Message}");
                }
            }));
        }

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.RadioButton rb || rb.Tag is not string page) return;
            Nav_ApplyPage(page);
        }

        /// <summary>V2-W7：统一页面路由（导航栏与命令面板共用）。</summary>
        private void NavigateTo(string page)
        {
            var target = page switch
            {
                "Deadline" => NavDeadline,
                "History" => NavHistory,
                "Stats" => NavStats,
                "Focus" => NavFocus,
                "Achievements" => NavAchievements,
                "Trash" => NavTrash,
                _ => NavDashboard
            };

            if (target.IsChecked == true)
            {
                Nav_ApplyPage(page); // 已在目标页：强制刷新内容
            }
            else
            {
                target.IsChecked = true; // 触发 Nav_Checked
            }
        }

        private void Nav_ApplyPage(string page)
        {
            var showDashboard = page == "Dashboard";
            var vm = DataContext as MainViewModel;
            DashboardPanel.Visibility = showDashboard ? Visibility.Visible : Visibility.Collapsed;
            DeadlinesPanel.Visibility = page == "Deadline" ? Visibility.Visible : Visibility.Collapsed;
            HistoryPanel.Visibility = page == "History" ? Visibility.Visible : Visibility.Collapsed;
            StatisticsPanel.Visibility = page == "Stats" ? Visibility.Visible : Visibility.Collapsed;
            FocusPanel.Visibility = page == "Focus" ? Visibility.Visible : Visibility.Collapsed;
            AchievementsPanel.Visibility = page == "Achievements" ? Visibility.Visible : Visibility.Collapsed;
            TrashPanel.Visibility = page == "Trash" ? Visibility.Visible : Visibility.Collapsed;

            if (vm != null)
            {
                vm.SelectedTabIndex = page switch
                {
                    "Deadline" => 1,
                    "History" => 2,
                    "Stats" => 3,
                    "Focus" => 4,
                    _ => 0
                };
            }

            if (showDashboard)
            {
                LoadChallenges(); // 回到今日页刷新挑战进度
                LoadDashboardHeader();
            }
            if (page == "Focus")
            {
                UpdateFocusPanel();
                LoadFocusTasks();
                InitNoiseControls();
            }
            if (page == "Stats")
            {
                LoadTrendChart();
                // v5.3：重进页面时刷新热力图（跟随主题/强调色与最新数据）
                if (vm != null)
                    vm.StatisticsViewModel.LoadHeatmap(vm.StatisticsViewModel.HeatmapYear);
                // v5.6.2：刷新输入统计卡并标定当前周期按钮高亮
                if (vm != null)
                {
                    vm.StatisticsViewModel.LoadTypingStats();
                    HighlightTypingPeriodButtons();
                }
            }
            if (page == "Trash") vm?.LoadDeletedTasks();
            if (page == "Achievements") LoadAchievements();

            AnimateActiveTabPanel();

            // V2-W8：今日页两个任务流 + 成就墙交错入场
            if (showDashboard)
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
                {
                    AnimationService.AnimateListStagger(DailyTasksListBox);
                    AnimationService.AnimateListStagger(DeadlineTasksListBox);
                }));
            }
            if (page == "Achievements")
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
                {
                    AnimationService.AnimateListStagger(AchievementsList, maxItems: 20);
                }));
            }
        }

        /// <summary>V2.1：仪表盘页头日期与问候语（对齐示意图"今天 / 8月22日 周五 · 下午好"）。</summary>
        private void LoadDashboardHeader()
        {
            if (DashboardSubtitleText == null) return;
            var now = DateTime.Now;
            string[] weeks = { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };
            var part = now.Hour switch
            {
                < 6 => "凌晨好",
                < 12 => "上午好",
                < 14 => "中午好",
                < 18 => "下午好",
                _ => "晚上好"
            };
            DashboardSubtitleText.Text = $"{now:M月d日} {weeks[(int)now.DayOfWeek]} · {part}";

            // V2 收尾：每日任务完成数环比（今日 vs 昨日）
            try
            {
                if (TodayDeltaText != null)
                {
                    var recs = DatabaseService.Instance.GetDailyCompletionRecords(2);
                    // R39（审查 M3/M12）：日期键统一 InvariantCulture
                    var todayKey = now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                    var yKey = now.AddDays(-1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                    var t = recs.TryGetValue(todayKey, out var a) ? a.Count : 0;
                    var y = recs.TryGetValue(yKey, out var b) ? b.Count : 0;
                    TodayDeltaText.Text = t == y ? "与昨日持平" : t > y ? $"↑ 比昨天 +{t - y}" : $"↓ 比昨天 {t - y}";
                    TodayDeltaText.Foreground = t >= y
                        ? TryFindResource("SuccessBrush") as Brush ?? Brushes.Green
                        : TryFindResource("DangerBrush") as Brush ?? Brushes.Red;
                }

                // R(review 修复 v5.6)：同步卡状态改由真实状态驱动（含失败原因/离线提示）
                UpdateSyncStatusUi();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadDashboardHeader extras error: {ex.Message}");
            }
        }

        /// <summary>R(review 修复 v5.6)：同步状态变化事件（可能来自后台同步线程，需调度回 UI 线程）。</summary>
        private void OnSyncStatusChanged(object? sender, SyncStatus status)
        {
            if (Dispatcher.CheckAccess())
                UpdateSyncStatusUi();
            else
                Dispatcher.BeginInvoke(new Action(UpdateSyncStatusUi));
        }

        /// <summary>
        /// R(review 修复 v5.6)：按真实同步状态刷新仪表盘"同步"卡片。
        /// 原实现硬编码"已同步到云端"绿点，未登录/离线/同步失败时仍然显示，
        /// 用户误以为数据安全上云——本次检查的实测场景中即为该误导掩盖了"上传被云端拒绝"。
        /// </summary>
        private void UpdateSyncStatusUi()
        {
            if (SyncStatusDot == null || SyncStatusText == null) return;
            try
            {
                var svc = SyncService.Instance;
                Brush dotBrush;
                string mainText;
                string? subText = null;

                switch (svc.Status)
                {
                    case SyncStatus.Syncing:
                        dotBrush = TryFindResource("AccentBrush") as Brush ?? Brushes.Orange;
                        mainText = " 正在同步...";
                        break;
                    case SyncStatus.Offline:
                        dotBrush = TryFindResource("TextTertiaryBrush") as Brush ?? Brushes.Gray;
                        mainText = " 离线，未同步";
                        break;
                    case SyncStatus.Error:
                        dotBrush = TryFindResource("DangerBrush") as Brush ?? Brushes.Red;
                        mainText = " 同步异常";
                        subText = svc.LastError;
                        break;
                    default:
                        if (!AuthService.Instance.IsLoggedIn)
                        {
                            dotBrush = TryFindResource("TextTertiaryBrush") as Brush ?? Brushes.Gray;
                            mainText = " 未登录，数据仅存本机";
                        }
                        else if (svc.LastSyncTime.HasValue)
                        {
                            dotBrush = TryFindResource("SuccessBrush") as Brush ?? Brushes.Green;
                            mainText = " 已同步到云端";
                        }
                        else
                        {
                            dotBrush = TryFindResource("TextTertiaryBrush") as Brush ?? Brushes.Gray;
                            mainText = " 尚未同步";
                        }
                        break;
                }

                SyncStatusDot.Fill = dotBrush;
                SyncStatusText.Text = mainText;

                var last = svc.LastSyncTime;
                if (SyncStampText != null)
                    SyncStampText.Text = subText
                        ?? (last.HasValue
                            ? $"上次同步 {last.Value.ToString("HH:mm")} · 每 30 秒自动"
                            : "每 30 秒自动同步 · 支持多设备合并");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateSyncStatusUi error: {ex.Message}");
            }
        }

        /// <summary>V2.1：明暗主题快捷切换。</summary>
        private void ThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.Instance.CurrentTheme = ThemeManager.IsCurrentlyDark() ? ThemeType.Light : ThemeType.Dark;
            UpdateThemeToggleGlyph();
        }

        private void UpdateThemeToggleGlyph()
        {
            if (ThemeToggleIcon == null) return;
            var dark = ThemeManager.IsCurrentlyDark();
            ThemeToggleIcon.Glyph = dark ? Icons.Sun : Icons.Moon;
            ThemeToggleButton.ToolTip = dark ? "切换到浅色主题" : "切换到深色主题";
        }

        /// <summary>V2.1：今日任务区"全部 ›"——跳转侧边栏完整列表。</summary>
        private void TodayAll_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                var sidebar = new MainWindow();
                sidebar.Show();
                Services.HotkeyService.Current?.ReRegisterHotkeys(sidebar);
                Close();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TodayAll error: {ex.Message}");
            }
        }

        /// <summary>导航栏底部"设置"入口（不参与页面分组）。</summary>
        private void NavExtra_Checked(object sender, RoutedEventArgs e)
        {
            // 立即弹回页面选中态，设置按钮不驻留选中
            if (sender is System.Windows.Controls.RadioButton rb)
                rb.IsChecked = false;
            SettingsButton_Click(sender, new RoutedEventArgs());
        }

        /// <summary>V2-W6：加载近 30 天完成趋势折线图。</summary>
        private void LoadTrendChart()        {
            try
            {
                var records = DatabaseService.Instance.GetDailyCompletionRecords(30);
                var values = new List<double>();
                var today = DateTime.Today;
                for (int i = 29; i >= 0; i--)
                {
                    // R39（审查 M3/M12）：InvariantCulture
                    var key = today.AddDays(-i).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                    values.Add(records.TryGetValue(key, out var set) ? set.Count : 0);
                }
                ThirtyDayChart.Values = values;
                if (TrendRangeText != null)
                    TrendRangeText.Text = $"{today.AddDays(-29):MM/dd} — {today:MM/dd}";
                if (TrendTotalText != null)
                    TrendTotalText.Text = $"30 天累计完成 {values.Sum():0} 次";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadTrendChart error: {ex.Message}");
            }
        }

        // ==================== v5.3 年度热力图 ====================

        /// <summary>v5.5：打开年度报告窗口（默认展示热力图当前年份）。</summary>
        private void YearReport_Click(object sender, RoutedEventArgs e)
        {
            var year = DataContext is MainViewModel vm
                ? vm.StatisticsViewModel.HeatmapYear
                : DateTime.Today.Year;
            var win = new YearReportWindow(year) { Owner = this };
            win.ShowDialog();
        }

        private void HeatmapPrevYear_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.StatisticsViewModel.LoadHeatmap(vm.StatisticsViewModel.HeatmapYear - 1);
        }

        private void HeatmapNextYear_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm
                && vm.StatisticsViewModel.HeatmapYear < DateTime.Today.Year)
                vm.StatisticsViewModel.LoadHeatmap(vm.StatisticsViewModel.HeatmapYear + 1);
        }

        // ==================== v5.6.2 输入统计周期切换 ====================

        private void TypingPeriod_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;
            if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out var index))
            {
                vm.StatisticsViewModel.SetTypingPeriod(index);
                HighlightTypingPeriodButtons();
            }
        }

        /// <summary>标定输入统计周期按钮高亮（选中态强调色底 + 白字）。</summary>
        private void HighlightTypingPeriodButtons()
        {
            var buttons = new[] { TypingBtn0, TypingBtn1, TypingBtn2, TypingBtn3 };
            int selected = (DataContext as MainViewModel)?.StatisticsViewModel.TypingPeriodIndex ?? 0;
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null) continue;
                buttons[i].Background = i == selected
                    ? TryFindResource("AccentBrush") as Brush ?? Brushes.Transparent
                    : Brushes.Transparent;
                buttons[i].Foreground = i == selected
                    ? Brushes.White
                    : (TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray);
            }
        }

        // ==================== v5.6 番茄白噪音 ====================

        /// <summary>专注页加载时同步白噪音按钮态与音量滑杆。</summary>
        private void InitNoiseControls()
        {
            var sound = SoundService.Instance;
            NoiseVolumeSlider.Value = sound.Volume * 100;
            UpdateNoiseChipStates();
        }

        private void NoiseKind_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button { Tag: string kind }) return;
            SoundService.Instance.Toggle(kind);
            UpdateNoiseChipStates();
        }

        private void NoiseVolume_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (NoiseVolumeSlider == null) return; // XAML 初始化期间触发
            // v5.6 审查修复：拖动期间只调内存音量（ValueChanged 高频），松开鼠标才落库
            SoundService.Instance.SetVolumeLive(NoiseVolumeSlider.Value / 100.0);
        }

        private void NoiseVolume_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (NoiseVolumeSlider == null) return;
            SoundService.Instance.SetVolume(NoiseVolumeSlider.Value / 100.0);
        }

        private void UpdateNoiseChipStates()
        {
            var sound = SoundService.Instance;
            foreach (var (btn, kind) in new[]
            {
                (NoiseRainBtn, "rain"), (NoiseStreamBtn, "stream"),
                (NoiseFireBtn, "fire"), (NoiseWhiteBtn, "white"),
            })
            {
                btn.Opacity = sound.IsPlaying && sound.CurrentKind == kind ? 1.0 : 0.55;
            }
        }

        /// <summary>
        /// v5.3 年度热力图：滚轮纵向输入转为横向翻页（不劫持页面滚动）。
        /// </summary>
        private void HeatmapScroll_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (sender is System.Windows.Controls.ScrollViewer sv && sv.ScrollableWidth > 0)
            {
                e.Handled = true;
                sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta);
            }
        }

        // ==================== v5.3 回收站 ====================

        private void PurgeAllTrash_Click(object sender, RoutedEventArgs e)
        {
            var count = (DataContext as MainViewModel)?.DeletedTasks.Count ?? 0;
            if (count == 0) return;
            var result = MessageBox.Show(this,
                $"将彻底删除回收站中的 {count} 个任务，且无法恢复。确定继续吗？",
                "清空回收站", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            // 逐条走命令路径，保持 VM 集合与库状态一致
            if (DataContext is MainViewModel vm)
                vm.PurgeAllTrashCommand.Execute(null);
        }

        /// <summary>对当前可见的内容面板播放淡入 + 轻微上移动画（用 RenderTransform，不影响布局）。</summary>
        private void AnimateActiveTabPanel()
        {
            if (AnimationService.ReduceMotion) return;
            FrameworkElement? panel = DashboardPanel?.Visibility == Visibility.Visible ? DashboardPanel
                : HistoryPanel?.Visibility == Visibility.Visible ? HistoryPanel
                : StatisticsPanel?.Visibility == Visibility.Visible ? StatisticsPanel
                : FocusPanel?.Visibility == Visibility.Visible ? FocusPanel
                : null;

            if (panel == null) return;

            var easing = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
            };
            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = easing
            };

            var translate = new System.Windows.Media.TranslateTransform(0, 8);
            panel.RenderTransform = translate;
            var slideUp = new System.Windows.Media.Animation.DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = easing
            };

            panel.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            translate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, slideUp);
        }

        /// <summary>Composer 类型分段切换（每日/截止）：仅控制截止日期选择器可见性，无需额外逻辑。</summary>
        private void SegmentType_Checked(object sender, RoutedEventArgs e)
        {
        }

        private void Priority_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string priority)
            {
                _selectedPriority = priority switch
                {
                    "High" => TaskPriority.High,
                    "Medium" => TaskPriority.Medium,
                    "Low" => TaskPriority.Low,
                    _ => TaskPriority.Medium
                };
            }
        }

        // ========== 任务详情对话框 ==========

        private void TaskListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBox listBox)
            {
                var listBoxItem = FindParent<ListBoxItem>((DependencyObject)e.OriginalSource);
                if (listBoxItem?.DataContext is TaskItem task && DataContext is MainViewModel vm)
                {
                    var dialog = new TaskDetailDialog(task, vm);
                    dialog.Owner = this;
                    dialog.ShowDialog();

                    // 保存成功且任务未完成时，清除该任务的已通知记录（M34）：
                    // 修改后的截止日期才能重新触发到期提醒
                    if (dialog.DialogResult == true && !task.IsCompleted)
                    {
                        NotificationService.Instance.ClearNotifiedTask(task.Id);
                    }
                }
            }
        }

        // ========== 拖拽排序 ==========

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
                // R48 修复（审查 M2）：未命中任务卡片时清空残留引用（移植 MainWindow 的 M25 修复）——
                // 原实现点过任务 A 后在空白处按下拖动，会误拖 A 执行重排
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
                if (_draggedTask != null && sender is ListBox listBox)
                {
                    _isDragging = true;
                    var data = new DataObject("TaskItem", _draggedTask);
                    try
                    {
                        DragDrop.DoDragDrop(listBox, data, DragDropEffects.Move);
                    }
                    finally
                    {
                        // R48：无论正常结束、ESC 取消还是 DoDragDrop 抛 COM 异常，
                        // 都要复位拖拽状态，避免悬挂的 _isDragging 短路后续 Move 判定
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
            _isDragging = false;
            _draggedTask = null;
        }

        // ========== 辅助方法 ==========

        private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is T typedParent)
                    return typedParent;
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }
    }
}
