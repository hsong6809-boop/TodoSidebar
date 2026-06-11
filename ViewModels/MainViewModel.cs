using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TodoSidebar.Models;
using TodoSidebar.Services;

namespace TodoSidebar.ViewModels
{
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        private readonly TaskService _taskService;
        private readonly DatabaseService _dbService;
        private readonly IMessageService _messageService;

        // 子 ViewModel
        public StatisticsViewModel StatisticsViewModel { get; }
        public SyncViewModel SyncViewModel { get; }

        [ObservableProperty]
        private int _selectedTabIndex;

        [ObservableProperty]
        private string _newTaskTitle = string.Empty;

        [ObservableProperty]
        private DateTime? _newTaskDeadline;

        [ObservableProperty]
        private TaskPriority _newTaskPriority = TaskPriority.Medium;

        [ObservableProperty]
        private string _newSubTaskTitle = string.Empty;

        // 搜索相关
        [ObservableProperty]
        private string _searchKeyword = string.Empty;

        [ObservableProperty]
        private bool _isSearchMode;

        public ObservableCollection<TaskItem> SearchResults { get; } = new();

        // 模板相关
        private readonly TaskTemplateService _templateService;
        public List<TaskTemplate> Templates => _templateService.GetTemplates();

        [ObservableProperty]
        private int _templatesCount;

        /// <summary>午夜刷新定时器，用于重置每日任务状态</summary>
        private DispatcherTimer? _midnightTimer;

        public ObservableCollection<TaskItem> DailyTasks { get; } = new();
        public ObservableCollection<TaskItem> DeadlineTasks { get; } = new();
        public ObservableCollection<TaskItem> HistoryTasks { get; } = new();
        public ObservableCollection<TaskItem> TodayCompletedTasks { get; } = new();
        public ObservableCollection<TaskItem> CurrentTasks { get; } = new();

        public int DailyTasksCount => DailyTasks.Count;
        public int DeadlineTasksCount => DeadlineTasks.Count;
        public int CurrentTasksCount => CurrentTasks.Count;
        public int HistoryTasksCount => HistoryTasks.Count;
        public int TodayCompletedTasksCount => TodayCompletedTasks.Count;

        public MainViewModel()
        {
            _dbService = DatabaseService.Instance;
            _taskService = new TaskService(_dbService, MessageService.Instance);
            _messageService = MessageService.Instance;
            _templateService = new TaskTemplateService();
            _templatesCount = Templates.Count;
            
            // 初始化子 ViewModel
            StatisticsViewModel = new StatisticsViewModel(_dbService);
            SyncViewModel = new SyncViewModel(SyncService.Instance, _messageService);
            SyncViewModel.OnSyncCompleted = () => LoadData();

            // 监听集合变化，自动刷新计数器
            DailyTasks.CollectionChanged += OnTaskCollectionChanged;
            DeadlineTasks.CollectionChanged += OnTaskCollectionChanged;
            HistoryTasks.CollectionChanged += OnTaskCollectionChanged;
            TodayCompletedTasks.CollectionChanged += OnTaskCollectionChanged;
            CurrentTasks.CollectionChanged += OnTaskCollectionChanged;

            // 午夜刷新：在每天零点自动重新加载每日任务
            ScheduleMidnightRefresh();

            LoadData();
        }

        private void ScheduleMidnightRefresh()
        {
            var now = DateTime.Now;
            var midnight = now.Date.AddDays(1);
            var msUntilMidnight = (midnight - now).TotalMilliseconds;

            _midnightTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(msUntilMidnight)
            };
            _midnightTimer.Tick += (s, e) =>
            {
                _midnightTimer?.Stop();
                LoadData();
                ScheduleMidnightRefresh();
            };
            _midnightTimer.Start();
        }

        private void OnTaskCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (sender == DailyTasks) OnPropertyChanged(nameof(DailyTasksCount));
            else if (sender == DeadlineTasks) OnPropertyChanged(nameof(DeadlineTasksCount));
            else if (sender == TodayCompletedTasks) OnPropertyChanged(nameof(TodayCompletedTasksCount));
            else if (sender == HistoryTasks) OnPropertyChanged(nameof(HistoryTasksCount));
            else if (sender == CurrentTasks) OnPropertyChanged(nameof(CurrentTasksCount));
        }

        private void LoadData()
        {
            LoadDailyTasks();
            LoadDeadlineTasks();
            LoadTodayCompletedTasks();
            LoadHistoryTasks();
            LoadCurrentTasks();
            StatisticsViewModel.LoadStatistics();
        }

        private void LoadDailyTasks()
        {
            DailyTasks.Clear();
            var todayCompletedIds = _dbService.GetTodayCompletedDailyTaskIds();
            foreach (var task in _taskService.GetDailyTasks())
            {
                task.IsTodayCompleted = todayCompletedIds.Contains(task.Id);
                // 今日已完成的每日任务在「今日完成」标签页展示，此处过滤避免重复
                if (task.IsTodayCompleted)
                    continue;
                DailyTasks.Add(task);
            }
        }

        private void LoadDeadlineTasks()
        {
            DeadlineTasks.Clear();
            foreach (var task in _taskService.GetDeadlineTasks())
                DeadlineTasks.Add(task);
        }

        private void LoadHistoryTasks()
        {
            HistoryTasks.Clear();
            foreach (var task in _taskService.GetHistoryTasks())
                HistoryTasks.Add(task);
        }
        
        private void LoadTodayCompletedTasks()
        {
            TodayCompletedTasks.Clear();
            var today = DateTime.Today;
            // 截止任务：正常查询已完成的
            var completedDeadlineTasks = _dbService.GetCompletedTasks(today, today.AddDays(1))
                .OrderByDescending(t => t.CompletedAt)
                .ToList();
            // 每日任务：查询今天在 DailyTaskCompletion 表中有记录的
            var completedDailyTasks = _taskService.GetTodayCompletedDailyTasks();
            
            foreach (var task in completedDeadlineTasks)
                TodayCompletedTasks.Add(task);
            foreach (var task in completedDailyTasks)
                TodayCompletedTasks.Add(task);
        }

        private void LoadCurrentTasks()
        {
            CurrentTasks.Clear();
            var todayCompletedIds = _dbService.GetTodayCompletedDailyTaskIds();
            foreach (var task in _taskService.GetCurrentTasks())
            {
                // 每日任务如果今天已完成，不显示在当前任务中
                if (task.Type == TaskType.Daily && todayCompletedIds.Contains(task.Id))
                    continue;
                task.IsTodayCompleted = todayCompletedIds.Contains(task.Id);
                CurrentTasks.Add(task);
            }
        }

        // ========== 任务 CRUD 命令 ==========

        [RelayCommand]
        private void AddDailyTask()
        {
            if (string.IsNullOrWhiteSpace(NewTaskTitle)) return;
            _taskService.AddTask(NewTaskTitle, TaskType.Daily, null, NewTaskPriority);
            NewTaskTitle = string.Empty;
            NewTaskPriority = TaskPriority.Medium;
            LoadDailyTasks();
            LoadCurrentTasks();
        }

        [RelayCommand]
        private void AddDeadlineTask()
        {
            if (string.IsNullOrWhiteSpace(NewTaskTitle)) return;
            
            if (NewTaskDeadline.HasValue && NewTaskDeadline.Value.Date < DateTime.Today)
            {
                _messageService.ShowWarning("截止日期不能早于今天！", "日期错误");
                return;
            }
            
            _taskService.AddTask(NewTaskTitle, TaskType.Deadline, NewTaskDeadline, NewTaskPriority);
            NewTaskTitle = string.Empty;
            NewTaskDeadline = null;
            NewTaskPriority = TaskPriority.Medium;
            LoadDeadlineTasks();
            LoadCurrentTasks();
        }

        [RelayCommand]
        private void CompleteTask(TaskItem? task)
        {
            if (task == null) return;
            try
            {
                _taskService.CompleteTask(task);
                LoadData();
            }
            catch (Exception ex)
            {
                _messageService.ShowError($"完成任务失败: {ex.Message}", "错误");
            }
        }

        [RelayCommand]
        private void DeleteTask(TaskItem? task)
        {
            if (task == null) return;
            _taskService.DeleteTask(task.Id);
            LoadData();
        }

        [RelayCommand]
        private void RestoreHistoryTask(TaskItem? task)
        {
            if (task == null) return;
            _taskService.UncompleteTask(task);
            LoadData();
        }

        // ========== 搜索操作 ==========

        [RelayCommand]
        private void Search()
        {
            if (string.IsNullOrWhiteSpace(SearchKeyword))
            {
                IsSearchMode = false;
                SearchResults.Clear();
                return;
            }

            IsSearchMode = true;
            SearchResults.Clear();
            var results = _dbService.SearchTasks(SearchKeyword);
            foreach (var task in results)
                SearchResults.Add(task);
        }

        [RelayCommand]
        private void ClearSearch()
        {
            SearchKeyword = string.Empty;
            IsSearchMode = false;
            SearchResults.Clear();
        }

        // ========== 模板操作 ==========

        [RelayCommand]
        private void ApplyTemplate(TaskTemplate? template)
        {
            if (template == null) return;
            var task = _templateService.CreateTaskFromTemplate(template);
            _dbService.InsertTask(task);
            LoadData();
        }

        // ========== 子任务操作 ==========

        [RelayCommand]
        private void AddSubTask(TaskItem? task)
        {
            if (task == null || string.IsNullOrWhiteSpace(NewSubTaskTitle)) return;
            
            var subTasks = SubTaskHelper.ParseSubTasks(task.SubTasksJson);
            subTasks.Add(new SubTask { Title = NewSubTaskTitle.Trim() });
            
            _taskService.UpdateSubTasks(task, SubTaskHelper.SerializeSubTasks(subTasks));
            NewSubTaskTitle = string.Empty;
            
            RefreshTaskProperties(task);
            LoadCurrentTasks();
        }

        [RelayCommand]
        private void ToggleSubTask(object? param)
        {
            TaskItem? task = null;
            SubTask? subTask = null;

            if (param is object[] args && args.Length >= 2)
            {
                task = args[0] as TaskItem;
                subTask = args[1] as SubTask;
            }
            else if (param is SubTask st)
            {
                subTask = st;
                task = CurrentTasks.FirstOrDefault(t => t.SubTasksList.Contains(subTask))
                    ?? DailyTasks.FirstOrDefault(t => t.SubTasksList.Contains(subTask))
                    ?? DeadlineTasks.FirstOrDefault(t => t.SubTasksList.Contains(subTask));
            }

            if (task == null || subTask == null) return;

            _taskService.UpdateSubTasks(task, SubTaskHelper.SerializeSubTasks(task.SubTasksList));
            RefreshTaskProperties(task);
            LoadCurrentTasks();
        }

        [RelayCommand]
        private void RemoveSubTask(object? param)
        {
            if (param is not object[] args || args.Length < 2) return;
            if (args[0] is not TaskItem task || args[1] is not int index) return;
            
            var subTasks = SubTaskHelper.ParseSubTasks(task.SubTasksJson);
            if (index >= 0 && index < subTasks.Count)
            {
                subTasks.RemoveAt(index);
                _taskService.UpdateSubTasks(task, SubTaskHelper.SerializeSubTasks(subTasks));
                RefreshTaskProperties(task);
                LoadCurrentTasks();
            }
        }

        /// <summary>
        /// 尝试立即刷新子任务相关 UI 绑定（后续 LoadData 也会完整刷新）
        /// </summary>
        private void RefreshTaskProperties(TaskItem task)
        {
            // 通知 UI 刷新子任务相关的绑定属性
            OnPropertyChanged(nameof(TaskItem.SubTasksList));
            OnPropertyChanged(nameof(TaskItem.SubTasksProgressText));
            OnPropertyChanged(nameof(TaskItem.HasSubTasks));
        }

        public void SaveSubTasksToDb(TaskItem task)
        {
            _taskService.UpdateSubTasks(task, task.SubTasksJson ?? "");
            LoadCurrentTasks();
        }

        public void SaveTaskToDb(TaskItem task)
        {
            _dbService.UpdateTask(task);
            LoadData();
        }

        // ========== 拖拽排序 ==========

        [RelayCommand]
        private void ReorderTasks(object? param)
        {
            if (param is not object[] args || args.Length < 2) return;
            if (args[0] is not TaskItem draggedTask || args[1] is not TaskItem targetTask) return;
            if (draggedTask.Id == targetTask.Id) return;

            var allTasks = CurrentTasks.OrderBy(t => t.SortOrder).ThenBy(t => t.CreatedAt).ToList();
            allTasks.Remove(draggedTask);
            
            int targetIndex = allTasks.IndexOf(targetTask);
            if (targetIndex < 0) return;
            
            allTasks.Insert(targetIndex, draggedTask);
            
            var orders = new List<(int id, int order)>();
            for (int i = 0; i < allTasks.Count; i++)
            {
                allTasks[i].SortOrder = i;
                orders.Add((allTasks[i].Id, i));
            }
            
            _dbService.UpdateTaskOrder(orders);
            LoadCurrentTasks();
        }

        [RelayCommand]
        private void SaveTaskOrder(object? param)
        {
            if (param is not IList<TaskItem> tasks) return;
            
            var orders = new List<(int id, int order)>();
            for (int i = 0; i < tasks.Count; i++)
                orders.Add((tasks[i].Id, i));
            _dbService.UpdateTaskOrder(orders);
        }

        public void Dispose()
        {
            DailyTasks.CollectionChanged -= OnTaskCollectionChanged;
            DeadlineTasks.CollectionChanged -= OnTaskCollectionChanged;
            HistoryTasks.CollectionChanged -= OnTaskCollectionChanged;
            CurrentTasks.CollectionChanged -= OnTaskCollectionChanged;
            TodayCompletedTasks.CollectionChanged -= OnTaskCollectionChanged;
            SyncViewModel.OnSyncCompleted = null;
            _midnightTimer?.Stop();
            _midnightTimer?.Stop();
        }
    }
}
