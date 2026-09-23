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
        // L21 修复：构造函数设置优先级默认值会触发 Priority_Checked，
        // 初始化完成前置 false，避免"仅查看就保存"产生多余 DB 写
        private bool _initialized;

        public TaskDetailDialog(TaskItem task, MainViewModel viewModel)
        {
            _task = task;
            _viewModel = viewModel;
            _subTasks = new ObservableCollection<SubTask>(SubTaskHelper.ParseSubTasks(task.SubTasksJson));
            _hasChanges = false;
            _initialized = false; // L21 修复：初始化期间不记录变更

            InitializeComponent();
            // U10/T9：Esc 关闭（IsCancel 已绑取消按钮）
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape)
                {
                    DialogResult = false;
                    Close();
                }
            };
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

            // 点击日期框任意位置直接弹出日历（WPF 默认只有右侧按钮能开）
            DeadlinePicker.PreviewMouseLeftButtonDown += DeadlinePicker_PreviewMouseLeftButtonDown;

            // 如果是每日任务，隐藏截止日期面板
            if (task.Type == TaskType.Daily)
            {
                DeadlinePanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                // v5.4 重复规则编辑器（仅截止任务）
                RecurrenceCombo.ItemsSource = RecurrenceRule.Options
                    .Select(o => new KeyValuePair<string, string>(o.Value, o.Label))
                    .ToList();
                RecurrenceCombo.SelectedValue = RecurrenceRule.Normalize(task.Recurrence) ?? "";
            }

            SubTasksItemsControl.ItemsSource = _subTasks;
            UpdateProgress();

            // L21 修复：初始化完成，此后用户操作才计入 _hasChanges
            _initialized = true;
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
            // IsChecked 为 TwoWay 绑定且 SubTask 实现 INPC，勾选状态已由绑定翻转，
            // 这里不再手动取反（否则会弹回），也无需 Items.Refresh
            _hasChanges = true;
            UpdateProgress();
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
            // Tag 直接绑定子任务对象，避免用 AlternationIndex 当索引（只在 0/1 循环导致删错条目）
            if (sender is Button button && button.Tag is SubTask subTask)
            {
                _subTasks.Remove(subTask);
                _hasChanges = true;
                UpdateProgress();
            }
        }

        private void Priority_Checked(object sender, RoutedEventArgs e)
        {
            // L21 修复：构造函数设置默认选中会触发本事件，初始化完成前忽略
            if (!_initialized) return;

            if (sender is RadioButton rb)
            {
                _hasChanges = true;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // R71（审查 M12）：先在局部变量里算出全部新值并完成所有校验/确认，
            // 最后才一次性写回共享 _task。原实现先改 Title/Priority、后弹截止日期确认，
            // 用户在确认框点"取消"时标题/优先级已落在列表共享对象上但未落库，
            // 造成内存与 DB 不一致、下次刷新回滚。
            var newTitle = TitleInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(newTitle))
            {
                MessageBox.Show("标题不能为空", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var newPriority = TaskPriority.Medium;
            if (PriorityHigh.IsChecked == true)
                newPriority = TaskPriority.High;
            else if (PriorityLow.IsChecked == true)
                newPriority = TaskPriority.Low;

            DateTime? newDeadline = null;
            string? selectedRecurrence = null;
            if (_task.Type == TaskType.Deadline)
            {
                // 存「所选日 00:00」；业务上截止时刻 = DeadlineEndOfDay（当天 24 点），
                // 与列表倒计时/逾期判定同一口径。选 8/29 → 8/29 全天可做，8/30 0 点起逾期。
                newDeadline = DeadlinePicker.SelectedDate?.Date;

                // L21 修复：编辑后的截止日期早于今天时二次确认（取消则不保存），
                // 与新增流程"截止日期不能早于今天"的校验行为对齐
                if (newDeadline.HasValue && newDeadline.Value.Date < DateTime.Today)
                {
                    var confirm = MessageBox.Show("截止日期已过去，确定保存吗？",
                        "确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (confirm != MessageBoxResult.Yes)
                        return;
                }

                // v5.4 保存重复规则（空串归一化为 null）
                selectedRecurrence = RecurrenceRule.Normalize(RecurrenceCombo.SelectedValue as string);
                // 月循环：保存时冻结锚点日（monthly → monthly:D），防止跨月漂移
                if (selectedRecurrence == RecurrenceRule.Monthly && newDeadline.HasValue)
                    selectedRecurrence = RecurrenceRule.FreezeMonthlyAnchor(newDeadline.Value, selectedRecurrence);
            }

            // ===== 全部确认通过，统一写回 =====
            // U4/T9：先写库再回写共享实例——原先先改 _task 后 Save，DB 失败会内存/库分叉
            var staged = new TaskItem
            {
                Id = _task.Id,
                Type = _task.Type,
                Title = newTitle,
                Priority = newPriority,
                Deadline = (_task.Type == TaskType.Deadline) ? newDeadline : _task.Deadline,
                Recurrence = (_task.Type == TaskType.Deadline) ? selectedRecurrence : _task.Recurrence,
                Description = _task.Description,
                Tags = _task.Tags,
                SortOrder = _task.SortOrder,
                IsCompleted = _task.IsCompleted,
                CreatedAt = _task.CreatedAt,
                CompletedAt = _task.CompletedAt,
                EstimatedMinutes = _task.EstimatedMinutes,
                ActualMinutes = _task.ActualMinutes,
                SubTasksJson = SubTaskHelper.SerializeSubTasks(_subTasks),
                SyncId = _task.SyncId,
                IsDirty = _task.IsDirty,
                IsDeleted = _task.IsDeleted,
                DeletedAt = _task.DeletedAt,
                LocalUpdatedAt = _task.LocalUpdatedAt,
                LastSyncedAt = _task.LastSyncedAt
            };
            _viewModel.SaveTaskToDb(staged);
            _task.Title = staged.Title;
            _task.Priority = staged.Priority;
            _task.Deadline = staged.Deadline;
            _task.Recurrence = staged.Recurrence;
            _task.SubTasksJson = staged.SubTasksJson;

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        /// <summary>v5.4：重复规则变化计入未保存状态。</summary>
        private void RecurrenceCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_initialized) _hasChanges = true;
        }

        private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
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

        private void DeadlinePicker_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not DatePicker picker || picker.IsDropDownOpen) return;
            // 已在编辑文本时保留光标定位，不抢焦点去弹日历
            if (picker.IsKeyboardFocusWithin) return;
            picker.IsDropDownOpen = true;
            e.Handled = true;
        }
    }
}
