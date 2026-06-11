using System;
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

namespace TodoSidebar
{
    public partial class MainWindow : Window
    {
        private bool _isCollapsed = false;
        private const double ExpandedWidth = 320;
        private const double CollapsedWidth = 3;
        
        // 悬停延迟定时器
        private readonly DispatcherTimer _hoverDelayTimer;
        private const int HoverDelayMilliseconds = 250;
        
        // 收起延迟定时器
        private readonly DispatcherTimer _collapseDelayTimer;
        private const int CollapseDelayMilliseconds = 300;
        
        private readonly DispatcherTimer _dateTimeTimer;
        private readonly DispatcherTimer _mouseCheckTimer;
        private DateTime _lastCollapseTime = DateTime.MinValue;
        private const int CollapseCooldownMs = 500;
        
        // 当前展开的任务
        private FrameworkElement? _expandedTaskCard;
        
        // 防重入锁
        private bool _isAnimating = false;
        private DateTime _lastClickTime = DateTime.MinValue;
        private const int ClickCooldownMs = 300;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = App.SharedViewModel;
            
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
            
            // 窗口失焦 → 收起（但鼠标仍在窗口内时不收起，防止点击按钮触发焦点变化）
            Deactivated += (_, _) =>
            {
                if (!_isCollapsed && !IsMouseOver)
                {
                    _lastCollapseTime = DateTime.Now;
                    CollapsePanel();
                }
            };

            // 鼠标检测在首次收起后启动，初始展开状态不需要
            _dateTimeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _dateTimeTimer.Tick += (s, args) => UpdateDateTime();
            _dateTimeTimer.Start();
            UpdateDateTime(); // 立即更新一次
            
            // 窗口关闭时清理定时器（ViewModel 由 App.OnExit 统一销毁）
            Closing += (s, e) =>
            {
                _hoverDelayTimer.Stop();
                _collapseDelayTimer.Stop();
                _dateTimeTimer.Stop();
                _mouseCheckTimer.Stop();
            };
        }

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
            DateTimeText.Text = $"{now:MM/dd} {dayOfWeek} {now:HH:mm:ss}";
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var helper = new WindowInteropHelper(this);
                var screen = System.Windows.Forms.Screen.FromHandle(helper.Handle);
                var screenWidth = screen.Bounds.Width;
                var screenHeight = screen.Bounds.Height;
                var screenLeft = screen.Bounds.X;
                var screenTop = screen.Bounds.Y;

                Left = screenLeft;
                Height = screenHeight * 0.66;
                Top = screenTop + (screenHeight - Height) / 2;
                Width = ExpandedWidth;

                SetWindowPos(helper.Handle, HWND_TOPMOST, (int)Left, (int)Top, (int)Width, (int)Height, SWP_SHOWWINDOW);
            }
            catch
            {
                Left = 0;
                Top = 100;
                Width = ExpandedWidth;
                Height = 600;
            }
        }

        #region 鼠标悬停展开/收起

        private void CollapseDelayTimer_Tick(object? sender, EventArgs e)
        {
            _collapseDelayTimer.Stop();
            // 再次检查鼠标是否仍在窗口内，防止计时器到期时鼠标已回到窗口
            if (!_isCollapsed && !IsMouseOver)
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
                var triggerRight = windowRect.Left + 30;

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
            catch (Exception) { /* DragMove may fail if mouse not captured */ }
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
                    // 退出登录
                    await AuthService.Instance.LogoutAsync();
                    
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
            
            // 安全检查：如果 MainPanel 宽度已经正确但不可见，强制恢复
            if (MainPanel.Width >= ExpandedWidth - 1 && MainPanel.Opacity < 0.1)
            {
                MainPanel.Opacity = 1;
            }
            
            AnimatePanel(true);
            
            // 兜底机制：1 秒后如果还没展开，强制恢复可见
            var failSafeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1000)
            };
            failSafeTimer.Tick += (s, e) =>
            {
                failSafeTimer.Stop();
                if (MainPanel.Opacity < 0.5 || MainPanel.Width < ExpandedWidth - 10)
                {
                    MainPanel.BeginAnimation(UIElement.OpacityProperty, null);
                    MainPanel.Opacity = 1;
                    MainPanel.Width = ExpandedWidth;
                    Width = ExpandedWidth;
                    _isAnimating = false;
                }
            };
            failSafeTimer.Start();
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
            catch
            {
                _isAnimating = false;
            }
        }

        #endregion

        #region 任务操作

        private void QuickAddButton_Click(object sender, RoutedEventArgs e)
        {
            AddQuickTask();
        }

        private void QuickAddInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AddQuickTask();
            }
        }

        private void AddQuickTask()
        {
            if (!CheckClickCooldown()) return;
            
            try
            {
                if (DataContext is MainViewModel vm && !string.IsNullOrWhiteSpace(QuickAddInput.Text))
                {
                    vm.NewTaskTitle = QuickAddInput.Text.Trim();
                    
                    if (QuickDeadlinePicker.SelectedDate.HasValue)
                    {
                        vm.NewTaskDeadline = QuickDeadlinePicker.SelectedDate;
                        vm.AddDeadlineTaskCommand.Execute(null);
                        QuickDeadlinePicker.SelectedDate = null;
                    }
                    else
                    {
                        vm.AddDailyTaskCommand.Execute(null);
                    }
                    
                    QuickAddInput.Text = "";
                    
                    // 动画延迟执行，确保UI已更新
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            if (TaskListBox.Items.Count > 0)
                            {
                                var lastItem = TaskListBox.Items[TaskListBox.Items.Count - 1];
                                var container = TaskListBox.ItemContainerGenerator.ContainerFromItem(lastItem) as FrameworkElement;
                                if (container != null)
                                {
                                    AnimationService.AnimateAdd(container);
                                }
                            }
                        }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"AnimateAdd error: {ex.Message}"); }
                    }), DispatcherPriority.Background);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AddQuickTask error: {ex.Message}");
            }
        }

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
        private const int HWND_TOPMOST = -1;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_SHOWWINDOW = 0x0040;

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, int hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

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
