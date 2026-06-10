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
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // AllowsTransparency="True" 模式，无需 DWM 调用
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
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
                MessageBox.Show($"打开统计窗口失败: {ex.Message}\n\n{ex.StackTrace}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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
                if (sender == TabDaily) vm.SelectedTabIndex = 0;
                else if (sender == TabDeadline) vm.SelectedTabIndex = 1;
                else if (sender == TabTemplate) vm.SelectedTabIndex = 2;
                else if (sender == TabHistory) vm.SelectedTabIndex = 3;
                else if (sender == TabStatistics)
                {
                    vm.SelectedTabIndex = 4;
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

        private void TemplateCard_Click(object sender, MouseButtonEventArgs e)
        {
            // 双击模板卡片可以快速应用
        }

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
