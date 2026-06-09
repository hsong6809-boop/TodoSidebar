using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    public partial class TaskDetailDialog : Window
    {
        private readonly TaskItem _task;
        private readonly MainViewModel _viewModel;
        private readonly ObservableCollection<SubTask> _subTasks;
        private bool _hasChanges;

        public TaskDetailDialog(TaskItem task, MainViewModel viewModel)
        {
            _task = task;
            _viewModel = viewModel;
            _subTasks = new ObservableCollection<SubTask>(SubTaskHelper.ParseSubTasks(task.SubTasksJson));
            _hasChanges = false;

            InitializeComponent();
            DataContext = this;

            // 初始化编辑区域
            TitleInput.Text = task.Title;
            
            // 设置优先级
            switch (task.Priority)
            {
                case TaskPriority.High:
                    PriorityHigh.IsChecked = true;
                    break;
                case TaskPriority.Medium:
                    PriorityMedium.IsChecked = true;
                    break;
                case TaskPriority.Low:
                    PriorityLow.IsChecked = true;
                    break;
            }

            // 设置截止日期
            if (task.Deadline.HasValue)
            {
                DeadlinePicker.SelectedDate = task.Deadline.Value;
            }

            // 如果是每日任务，隐藏截止日期面板
            if (task.Type == TaskType.Daily)
            {
                DeadlinePanel.Visibility = Visibility.Collapsed;
            }

            SubTasksItemsControl.ItemsSource = _subTasks;
            UpdateProgress();
        }

        private void UpdateProgress()
        {
            var total = _subTasks.Count;
            var completed = _subTasks.Count(s => s.IsCompleted);
            ProgressText.Text = total > 0 ? $"{completed}/{total}" : "无子任务";
            ProgressText.Foreground = new SolidColorBrush(
                total > 0 && completed == total
                    ? Color.FromRgb(0, 196, 140) // 全部完成 - 绿色
                    : Color.FromRgb(91, 95, 233)); // 进行中 - 紫色
        }

        private void SubTaskCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.DataContext is SubTask subTask)
            {
                subTask.IsCompleted = !subTask.IsCompleted;
                _hasChanges = true;
                UpdateProgress();
                // 刷新 ItemsSource 显示
                SubTasksItemsControl.ItemsSource = null;
                SubTasksItemsControl.ItemsSource = _subTasks;
            }
        }

        private void AddSubTaskButton_Click(object sender, RoutedEventArgs e)
        {
            AddSubTask();
        }

        private void NewSubTaskInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AddSubTask();
            }
        }

        private void AddSubTask()
        {
            var title = NewSubTaskInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(title)) return;

            _subTasks.Add(new SubTask { Title = title });
            _hasChanges = true;
            NewSubTaskInput.Text = "";
            UpdateProgress();
        }

        private void DeleteSubTaskButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int index)
            {
                if (index >= 0 && index < _subTasks.Count)
                {
                    _subTasks.RemoveAt(index);
                    _hasChanges = true;
                    UpdateProgress();
                }
            }
        }

        private void Priority_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb)
            {
                _hasChanges = true;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // 保存标题
            var newTitle = TitleInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(newTitle))
            {
                MessageBox.Show("标题不能为空", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (newTitle != _task.Title)
            {
                _task.Title = newTitle;
                _hasChanges = true;
            }

            // 保存优先级
            var newPriority = TaskPriority.Medium;
            if (PriorityHigh.IsChecked == true)
                newPriority = TaskPriority.High;
            else if (PriorityLow.IsChecked == true)
                newPriority = TaskPriority.Low;

            if (newPriority != _task.Priority)
            {
                _task.Priority = newPriority;
                _hasChanges = true;
            }

            // 保存截止日期
            if (_task.Type == TaskType.Deadline)
            {
                var newDeadline = DeadlinePicker.SelectedDate;
                if (newDeadline != _task.Deadline)
                {
                    _task.Deadline = newDeadline;
                    _hasChanges = true;
                }
            }

            // 保存子任务
            if (_hasChanges)
            {
                _task.SubTasksJson = SubTaskHelper.SerializeSubTasks(_subTasks);
                _viewModel.SaveTaskToDb(_task);
            }

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            DragMove();
        }
    }
}
