using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace TodoSidebar.Services
{
    public class NotificationService
    {
        private static NotificationService? _instance;
        public static NotificationService Instance => _instance ??= new NotificationService();

        private readonly DispatcherTimer _checkTimer;
        private readonly DispatcherTimer _midnightTimer;
        private readonly HashSet<int> _notifiedTasks = new();
        private readonly DatabaseService _dbService;
        private readonly TaskService _taskService;
        private DateTime _lastCheckDate = DateTime.Today;

        public event EventHandler<string>? NotificationRequested;

        private NotificationService()
        {
            _dbService = DatabaseService.Instance;
            _taskService = new TaskService(_dbService);

            _checkTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(1) // 每分钟检查一次
            };
            _checkTimer.Tick += CheckTimer_Tick;

            // 修复 B3：每天零点清空已通知列表 —— 用日期变化检测代替整点巧合判断
            // 旧实现 now.Hour==0 && now.Minute==0 几乎不可能被 DispatcherTimer 恰好命中
            _midnightTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(60)
            };
            _midnightTimer.Tick += (s, e) =>
            {
                var now = DateTime.Now;
                if (now.Date != _lastCheckDate)
                {
                    _lastCheckDate = now.Date;
                    _notifiedTasks.Clear();
                    System.Diagnostics.Debug.WriteLine($"[NotificationService] 跨天，已清空通知列表: {now:yyyy-MM-dd}");
                }
            };
            _midnightTimer.Start();
        }

        public void Start()
        {
            _checkTimer.Start();
            CheckNotifications();
        }

        public void Stop()
        {
            _checkTimer.Stop();
            _midnightTimer.Stop();
        }

        private void CheckTimer_Tick(object? sender, EventArgs e)
        {
            CheckNotifications();
        }

        private void CheckNotifications()
        {
            try
            {
                var deadlineTasks = _taskService.GetDeadlineTasks();

                // 修复 F3：通知聚合 —— 收集所有待通知任务，一条通知展示，避免窗口重叠
                var expiredTasks = new List<string>();
                var dueSoonTasks = new List<string>();
                var dueTodayTasks = new List<string>();

                foreach (var task in deadlineTasks)
                {
                    if (task.Deadline == null || _notifiedTasks.Contains(task.Id))
                        continue;

                    var timeLeft = task.Deadline.Value - DateTime.Now;

                    // 已过期
                    if (timeLeft.TotalMinutes <= 0)
                    {
                        expiredTasks.Add(task.Title);
                        _notifiedTasks.Add(task.Id);
                    }
                    // 即将到期（1小时内）
                    else if (timeLeft.TotalHours <= 1)
                    {
                        dueSoonTasks.Add($"{task.Title}（{(int)timeLeft.TotalMinutes} 分钟后）");
                        _notifiedTasks.Add(task.Id);
                    }
                    // 今天到期
                    else if (task.Deadline.Value.Date == DateTime.Today && timeLeft.TotalHours > 1)
                    {
                        dueTodayTasks.Add(task.Title);
                        _notifiedTasks.Add(task.Id);
                    }
                }

                // 聚合显示
                var notifications = new List<string>();
                if (expiredTasks.Count > 0)
                    notifications.Add($"🔴 已过期（{expiredTasks.Count}）：{string.Join("、", expiredTasks.Take(5))}{(expiredTasks.Count > 5 ? " 等" : "")}");
                if (dueSoonTasks.Count > 0)
                    notifications.Add($"⏰ 即将到期（{dueSoonTasks.Count}）：{string.Join("、", dueSoonTasks.Take(5))}{(dueSoonTasks.Count > 5 ? " 等" : "")}");
                if (dueTodayTasks.Count > 0)
                    notifications.Add($"📅 今日到期（{dueTodayTasks.Count}）：{string.Join("、", dueTodayTasks.Take(5))}{(dueTodayTasks.Count > 5 ? " 等" : "")}");

                if (notifications.Count > 0)
                {
                    ShowNotification("⏰ 任务提醒", string.Join("\n", notifications));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"通知检查失败: {ex.Message}");
            }
        }

        public void ShowNotification(string title, string message)
        {
            NotificationRequested?.Invoke(this, $"{title}\n{message}");

            Application.Current?.Dispatcher.Invoke(() =>
            {
                var window = new NotificationWindow(title, message);
                window.Show();
            });
        }

        public void ClearNotifiedTask(int taskId)
        {
            _notifiedTasks.Remove(taskId);
        }
    }

    // 简单的通知窗口
    public class NotificationWindow : Window
    {
        private const int NotificationWidth = 300;
        private const int NotificationHeight = 100;
        private const double AutoCloseSeconds = 3;

        public NotificationWindow(string title, string message)
        {
            Title = title;
            Width = NotificationWidth;
            Height = NotificationHeight;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;

            var border = new System.Windows.Controls.Border
            {
                Background = (System.Windows.Media.Brush)Application.Current.Resources["CardBrush"],
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16),
                BorderBrush = (System.Windows.Media.Brush)Application.Current.Resources["AccentBrush"],
                BorderThickness = new Thickness(0, 0, 3, 0),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 10,
                    ShadowDepth = 2,
                    Opacity = 0.3
                }
            };

            var stackPanel = new System.Windows.Controls.StackPanel();

            var titleBlock = new System.Windows.Controls.TextBlock
            {
                Text = title,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["TextBrush"]
            };

            var messageBlock = new System.Windows.Controls.TextBlock
            {
                Text = message,
                FontSize = 12,
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["TextSecondaryBrush"],
                TextWrapping = TextWrapping.Wrap
            };

            stackPanel.Children.Add(titleBlock);
            stackPanel.Children.Add(messageBlock);
            border.Child = stackPanel;
            Content = border;

            // 位置：当前屏幕右下角（适配多屏）
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 20;
            Top = workArea.Bottom - Height - 60;

            // 自动关闭
            var closeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(AutoCloseSeconds)
            };
            closeTimer.Tick += (s, e) =>
            {
                closeTimer.Stop();
                Close();
            };
            closeTimer.Start();

            // 点击关闭
            MouseLeftButtonDown += (s, e) => Close();
        }
    }
}
