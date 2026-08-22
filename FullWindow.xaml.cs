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

            // P2：真实亚克力背板（失败静默降级为半透明纯色）
            Loaded += (_, _) => DwmBackdropHelper.ApplyMainShellAcrylic(this);

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

            base.OnClosed(e);
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

            public ChallengeItem(TodoSidebar.Models.DailyChallenge c)
            {
                Icon = c.Icon;
                Title = c.Title;
                ProgressText = $"{c.Progress}/{c.Target}";
                StatusText = c.Completed ? "✓" : "";
                ProgressFraction = Math.Clamp(c.Target > 0 ? (double)c.Progress / c.Target : 0, 0, 1);
            }
        }

        #endregion

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

            FocusStartButton.Content = pomo.State == PomodoroState.Paused ? "▶ 继续" : "▶ 开始专注";

            // 今日统计读缓存（M35）：Tick 每秒触发本方法，统计文本仅每 30 秒
            // （或状态变化/会话完成事件强制失效后）重新查库一次
            RefreshTodayStatsCache();
            var (completed, interrupted, minutes) = _cachedTodayStats;
            FocusTodayText.Text = $"今日番茄：{completed} 个 · 专注 {minutes} 分钟{(interrupted > 0 ? $" · 中断 {interrupted}" : "")}";
            FocusRoundText.Text = $"本轮：{completed % PomodoroService.RoundsPerCycle}/{PomodoroService.RoundsPerCycle} · 每日目标 {completed}/{PomodoroService.DailyTarget}";

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

            var result = MessageBox.Show("停止当前番茄将视为中断，不获得经验。确定停止吗？",
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
                    // 退出登录
                    await AuthService.Instance.LogoutAsync();

                    // 释放共享 ViewModel 并停止后台服务，避免登出后空转
                    App.StopBackgroundServices();
                    
                    // 关闭主窗口，显示登录窗口
                    var loginWindow = new LoginWindow();
                    loginWindow.Show();
                    Close();
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
                vm.NewTaskPriority = _selectedPriority;
                
                if (TabDaily.IsChecked == true)
                {
                    vm.AddDailyTaskCommand.Execute(null);
                    AnimateLastItem(DailyTasksListBox);
                }
                else if (TabDeadline.IsChecked == true)
                {
                    vm.AddDeadlineTaskCommand.Execute(null);
                    AnimateLastItem(DeadlineTasksListBox);
                }
            }
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

            var showDashboard = page == "Dashboard";
            DashboardPanel.Visibility = showDashboard ? Visibility.Visible : Visibility.Collapsed;
            HistoryPanel.Visibility = page == "History" ? Visibility.Visible : Visibility.Collapsed;
            StatisticsPanel.Visibility = page == "Stats" ? Visibility.Visible : Visibility.Collapsed;
            FocusPanel.Visibility = page == "Focus" ? Visibility.Visible : Visibility.Collapsed;

            if (DataContext is MainViewModel vm)
            {
                vm.SelectedTabIndex = showDashboard ? 0 : page == "History" ? 2 : page == "Stats" ? 3 : 4;
            }

            if (showDashboard) LoadChallenges(); // 回到今日页刷新挑战进度
            if (page == "Focus")
            {
                UpdateFocusPanel();
                LoadFocusTasks();
            }

            AnimateActiveTabPanel();
        }

        /// <summary>导航栏底部"设置"入口（不参与页面分组）。</summary>
        private void NavExtra_Checked(object sender, RoutedEventArgs e)
        {
            // 立即弹回页面选中态，设置按钮不驻留选中
            if (sender is System.Windows.Controls.RadioButton rb)
                rb.IsChecked = false;
            SettingsButton_Click(sender, new RoutedEventArgs());
        }

        /// <summary>对当前可见的内容面板播放淡入 + 轻微上移动画（用 RenderTransform，不影响布局）。</summary>
        private void AnimateActiveTabPanel()
        {
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
                    DragDrop.DoDragDrop(listBox, data, DragDropEffects.Move);
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
