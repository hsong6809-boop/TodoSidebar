using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TodoSidebar.Helpers;
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

        public FullWindow()
        {
            InitializeComponent();
            DataContext = App.SharedViewModel;

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

            public ChallengeItem(TodoSidebar.Models.DailyChallenge c)
            {
                Icon = c.Icon;
                Title = c.Title;
                ProgressText = $"{c.Progress}/{c.Target} · 奖励 +{c.Xp} XP";
                StatusText = c.Completed ? "✅ 已完成" : "⏳ 进行中";
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

        private void OnFocusStateChanged(object? sender, PomodoroState state) => UpdateFocusPanel();

        private void OnFocusSessionCompleted(object? sender, PomodoroSessionCompletedEventArgs e)
        {
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

            var (completed, interrupted, minutes) = pomo.GetTodayStats();
            FocusTodayText.Text = $"今日番茄：{completed} 个 · 专注 {minutes} 分钟{(interrupted > 0 ? $" · 中断 {interrupted}" : "")}";
            FocusRoundText.Text = $"本轮：{completed % PomodoroService.RoundsPerCycle}/{PomodoroService.RoundsPerCycle} · 每日目标 {completed}/{PomodoroService.DailyTarget}";
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
            if (listBox.Items.Count > 0)
            {
                var lastItem = listBox.Items[listBox.Items.Count - 1];
                var container = listBox.ItemContainerGenerator.ContainerFromItem(lastItem) as FrameworkElement;
                if (container != null)
                {
                    AnimationService.AnimateAdd(container);
                }
            }
        }

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                if (sender == TabDaily)
                {
                    vm.SelectedTabIndex = 0;
                    LoadChallenges(); // 切回每日 Tab 刷新挑战进度
                }
                else if (sender == TabDeadline) vm.SelectedTabIndex = 1;
                else if (sender == TabHistory) vm.SelectedTabIndex = 2;
                else if (sender == TabStatistics)
                {
                    vm.SelectedTabIndex = 3;
                }
                else if (sender == TabFocus)
                {
                    vm.SelectedTabIndex = 4;
                    UpdateFocusPanel();
                    LoadFocusTasks();
                }
            }
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
