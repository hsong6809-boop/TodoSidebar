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
        private const double CollapsedWidth = 6;
        
        // 悬停延迟定时器
        private readonly DispatcherTimer _hoverDelayTimer;
        private const int HoverDelayMilliseconds = 400;
        
        // 收起延迟定时器
        private readonly DispatcherTimer _collapseDelayTimer;
        private const int CollapseDelayMilliseconds = 300;
        
        // 鼠标轮询定时器
        private readonly DispatcherTimer _mousePollTimer;
        private bool _wasMouseOver = false;
        
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
            
            // 初始化鼠标轮询定时器
            _mousePollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _mousePollTimer.Tick += MousePollTimer_Tick;
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
                
                // 启动鼠标轮询
                _mousePollTimer.Start();
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

        private void MousePollTimer_Tick(object? sender, EventArgs e)
        {
            bool isMouseOver = IsMouseOver;
            
            // 鼠标刚移入
            if (isMouseOver && !_wasMouseOver)
            {
                _collapseDelayTimer.Stop(); // 取消待执行的收起
                _hoverDelayTimer.Stop();
                if (_isCollapsed)
                {
                    _hoverDelayTimer.Start();
                }
            }
            // 鼠标刚移出
            else if (!isMouseOver && _wasMouseOver)
            {
                _hoverDelayTimer.Stop();
                if (!_isCollapsed)
                {
                    _collapseDelayTimer.Start();
                }
            }
            
            _wasMouseOver = isMouseOver;
        }

        private void CollapseDelayTimer_Tick(object? sender, EventArgs e)
        {
            _collapseDelayTimer.Stop();
            if (!_isCollapsed)
            {
                CollapsePanel();
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
            catch { }
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
            AnimatePanel(false);
        }

        private void ExpandPanel()
        {
            if (_isAnimating) return;
            _isCollapsed = false;
            AnimatePanel(true);
        }

        private void AnimatePanel(bool expand)
        {
            if (_isAnimating) return;
            _isAnimating = true;

            try
            {
                BeginAnimation(WidthProperty, null);
                MainPanel.BeginAnimation(WidthProperty, null);

                var duration = TimeSpan.FromMilliseconds(300);
                var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

                var targetWidth = expand ? ExpandedWidth : CollapsedWidth;
                var panelTargetWidth = expand ? ExpandedWidth : 0;

                var winAnim = new DoubleAnimation(targetWidth, duration) { EasingFunction = easing };
                winAnim.Completed += (s, e) => 
                { 
                    Width = targetWidth; 
                    BeginAnimation(WidthProperty, null);
                    _isAnimating = false;
                };
                BeginAnimation(WidthProperty, winAnim);

                var panelAnim = new DoubleAnimation(panelTargetWidth, duration) { EasingFunction = easing };
                panelAnim.Completed += (s, e) => 
                { 
                    MainPanel.Width = panelTargetWidth; 
                    MainPanel.BeginAnimation(WidthProperty, null); 
                };
                MainPanel.BeginAnimation(WidthProperty, panelAnim);
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
                        catch { }
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
                    catch { }
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
                catch { }
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
            catch { }
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
            catch { }

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
    }
}
