using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;

namespace TodoSidebar.Services
{
    public class NotificationService
    {
        private static NotificationService? _instance;
        public static NotificationService Instance => _instance ??= new NotificationService();

        private readonly DispatcherTimer _checkTimer;
        private readonly HashSet<int> _notifiedTasks = new();
        private readonly DatabaseService _dbService;

        public event EventHandler<string>? NotificationRequested;

        private NotificationService()
        {
            _dbService = DatabaseService.Instance;
            

            _checkTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(1) // 每分钟检查一次
            };
            _checkTimer.Tick += CheckTimer_Tick;
        }

        public void Start()
        {
            _checkTimer.Start();
            CheckNotifications(); // 立即检查一次
        }

        public void Stop()
        {
            _checkTimer.Stop();
        }

        private void CheckTimer_Tick(object? sender, EventArgs e)
        {
            CheckNotifications();
        }

        private void CheckNotifications()
        {
            try
            {
                var taskService = new TaskService(_dbService);
                var deadlineTasks = taskService.GetDeadlineTasks();

                foreach (var task in deadlineTasks)
                {
                    if (task.Deadline == null || _notifiedTasks.Contains(task.Id))
                        continue;

                    var timeLeft = task.Deadline.Value - DateTime.Now;

                    // 即将到期（1小时内）
                    if (timeLeft.TotalHours <= 1 && timeLeft.TotalMinutes > 0)
                    {
                        ShowNotification($"⏰ 任务即将到期", $"「{task.Title}」将在 {(int)timeLeft.TotalMinutes} 分钟后到期");
                        _notifiedTasks.Add(task.Id);
                    }
                    // 今天到期
                    else if (task.Deadline.Value.Date == DateTime.Today && timeLeft.TotalHours > 1)
                    {
                        ShowNotification($"📅 今日到期任务", $"「{task.Title}」今天到期");
                        _notifiedTasks.Add(task.Id);
                    }
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

            // 使用系统通知
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
        public NotificationWindow(string title, string message)
        {
            Title = title;
            Width = 300;
            Height = 100;
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

            // 位置：右下角
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var screenHeight = SystemParameters.PrimaryScreenHeight;
            Left = screenWidth - Width - 20;
            Top = screenHeight - Height - 60;

            // 3秒后自动关闭
            var closeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
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
