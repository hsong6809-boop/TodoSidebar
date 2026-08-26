using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace TodoSidebar.Services
{
    public class NotificationService
    {
        private static readonly Lazy<NotificationService> _lazy = new(() => new NotificationService());
        public static NotificationService Instance => _lazy.Value;

        private readonly DispatcherTimer _checkTimer;
        private readonly DispatcherTimer _midnightTimer;
        private readonly HashSet<int> _notifiedTasks = new();
        private readonly object _notifiedLock = new();
        private DateTime _lastClearDate = DateTime.Today;
        private readonly DatabaseService _dbService;
        private readonly TaskService _taskService;

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

            // 每天零点清空已通知列表（按日期判断，不依赖整点命中）
            _midnightTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(1)
            };
            _midnightTimer.Tick += (s, e) =>
            {
                var today = DateTime.Today;
                if (today != _lastClearDate)
                {
                    lock (_notifiedLock)
                    {
                        _notifiedTasks.Clear();
                    }
                    _lastClearDate = today;
                }
            };
            _midnightTimer.Start();
        }

        public void Start()
        {
            // L20 修复：Start 同时恢复检查与零点两个计时器（与 Stop 成对）
            _checkTimer.Start();
            _midnightTimer.Start();
            CheckNotifications();
        }

        public void Stop()
        {
            // L20 修复：原实现漏停 _midnightTimer，Stop 后零点清理逻辑仍在后台运行
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
                // R24 修复（审查 H6）：通知检查必须包含已逾期任务——
                // 原实现复用 GetDeadlineTasks() 的"过滤过期"口径，
                // 导致下方「🔴 任务已过期」分支永远不可达，整个过期提醒功能静默失效。
                // _notifiedTasks 去重保证同一任务只弹一次。
                var deadlineTasks = _taskService.GetDeadlineTasks(includeOverdue: true);

                foreach (var task in deadlineTasks)
                {
                    if (task.Deadline == null)
                        continue;

                    lock (_notifiedLock)
                    {
                        if (_notifiedTasks.Contains(task.Id))
                            continue;
                    }

                    // V2：到期时刻 = 截止日 24 点（而非当日 0 点）
                    var timeLeft = task.DeadlineEndOfDay - DateTime.Now;

                    // 已过期
                    if (timeLeft.TotalMinutes <= 0)
                    {
                        ShowNotification($"🔴 任务已过期", $"「{task.Title}」已经过期");
                        lock (_notifiedLock) { _notifiedTasks.Add(task.Id); }
                    }
                    // 即将到期（1小时内）
                    else if (timeLeft.TotalHours <= 1)
                    {
                        ShowNotification($"⏰ 任务即将到期", $"「{task.Title}」将在 {(int)timeLeft.TotalMinutes} 分钟后到期");
                        lock (_notifiedLock) { _notifiedTasks.Add(task.Id); }
                    }
                    // 今天到期
                    else if (task.Deadline.Value.Date == DateTime.Today && timeLeft.TotalHours > 1)
                    {
                        ShowNotification($"📅 今日到期任务", $"「{task.Title}」今天到期");
                        lock (_notifiedLock) { _notifiedTasks.Add(task.Id); }
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

            Application.Current?.Dispatcher.Invoke(() =>
            {
                var window = new NotificationWindow(title, message);
                window.Show();
            });
        }

        public void ClearNotifiedTask(int taskId)
        {
            lock (_notifiedLock)
            {
                _notifiedTasks.Remove(taskId);
            }
        }
    }

    // 简单的通知窗口
    public class NotificationWindow : Window
    {
        private const int NotificationWidth = 300;
        private const int NotificationHeight = 100;
        private const double AutoCloseSeconds = 3;

        // L20 修复：静态活跃通知计数，多条通知按序号×80px 垂直错开，不再同位置重叠
        private static int _activeNotifications;
        private readonly int _slotIndex;

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
            // L20 修复：占用一个层叠序号并按序号×80px 下移 Top（构造时计数，早于 Show 完成定位无跳动）
            _slotIndex = Interlocked.Increment(ref _activeNotifications) - 1;
            Top = workArea.Bottom - Height - 60 - (_slotIndex * 80);

            // L20 修复：窗口关闭时归还层叠序号
            Closed += (s, e) => Interlocked.Decrement(ref _activeNotifications);

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

            // 点击关闭：停止自动关闭计时器，避免残留引用
            MouseLeftButtonDown += (s, e) =>
            {
                closeTimer.Stop();
                Close();
            };
        }
    }
}
