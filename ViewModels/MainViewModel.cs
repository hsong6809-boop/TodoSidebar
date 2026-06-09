using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Text.Json;
using System.Windows;
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
        private readonly SyncService _syncService;

        // 统计视图模型
        public StatisticsViewModel StatisticsViewModel { get; }

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

        // 同步相关
        [ObservableProperty]
        private bool _isSyncing;
        
        [ObservableProperty]
        private string _syncStatusText = string.Empty;
        
        [ObservableProperty]
        private DateTime? _lastSyncTime;

        public ObservableCollection<TaskItem> SearchResults { get; } = new();

        // 模板相关
        private readonly TaskTemplateService _templateService;
        public List<TaskTemplate> Templates => _templateService.GetTemplates();

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
            _taskService = new TaskService(_dbService);
            _messageService = MessageService.Instance;
            _templateService = new TaskTemplateService();
            _syncService = SyncService.Instance;
            
            // 初始化统计视图模型
            StatisticsViewModel = new StatisticsViewModel(_dbService);

            // 监听集合变化，自动刷新计数器
            DailyTasks.CollectionChanged += OnTaskCollectionChanged;
            DeadlineTasks.CollectionChanged += OnTaskCollectionChanged;
            HistoryTasks.CollectionChanged += OnTaskCollectionChanged;
            TodayCompletedTasks.CollectionChanged += OnTaskCollectionChanged;
            CurrentTasks.CollectionChanged += OnTaskCollectionChanged;

            LoadData();
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
            
            // 刷新统计数据
            StatisticsViewModel.LoadStatistics();
        }

        private void LoadDailyTasks()
        {
            DailyTasks.Clear();
            foreach (var task in _taskService.GetDailyTasks())
                DailyTasks.Add(task);
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
            var completedTasks = _dbService.GetCompletedTasks(today, today.AddDays(1))
                .OrderByDescending(t => t.CompletedAt)
                .ToList();
            foreach (var task in completedTasks)
                TodayCompletedTasks.Add(task);
        }

        private void LoadCurrentTasks()
        {
            CurrentTasks.Clear();
            foreach (var task in _taskService.GetCurrentTasks())
                CurrentTasks.Add(task);
        }

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
            
            // 验证截止日期
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
            _taskService.CompleteTask(task);
            LoadData();
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
            
            // 触发属性刷新
            RefreshTaskProperties(task);
            LoadCurrentTasks();
        }

        [RelayCommand]
        private void ToggleSubTask(object? param)
        {
            // param 可能是 object[] {TaskItem, SubTask} 或直接是 SubTask
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
                // 从当前任务列表中找到包含此子任务的父任务
                task = CurrentTasks.FirstOrDefault(t => t.SubTasksList.Contains(subTask))
                    ?? DailyTasks.FirstOrDefault(t => t.SubTasksList.Contains(subTask))
                    ?? DeadlineTasks.FirstOrDefault(t => t.SubTasksList.Contains(subTask));
            }

            if (task == null || subTask == null) return;

            // UI 的 IsChecked 绑定已经更新了 subTask.IsCompleted
            // 直接把当前子任务列表序列化保存即可
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

        private void RefreshTaskProperties(TaskItem task)
        {
            // 触发计算属性刷新
            task.SubTasksJson = task.SubTasksJson; // 触发重新计算
            OnPropertyChanged(nameof(TaskItem.SubTasksList));
            OnPropertyChanged(nameof(TaskItem.SubTasksProgressText));
            OnPropertyChanged(nameof(TaskItem.HasSubTasks));
        }

        // 保存子任务到数据库（供对话框调用）
        public void SaveSubTasksToDb(TaskItem task)
        {
            _taskService.UpdateSubTasks(task, task.SubTasksJson ?? "");
            LoadCurrentTasks();
        }

        // 保存任务修改（供对话框调用）
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

            // 获取当前列表中的所有任务（按 SortOrder 排序）
            var allTasks = CurrentTasks.OrderBy(t => t.SortOrder).ThenBy(t => t.CreatedAt).ToList();
            
            // 移除被拖拽的任务
            allTasks.Remove(draggedTask);
            
            // 找到目标位置
            int targetIndex = allTasks.IndexOf(targetTask);
            if (targetIndex < 0) return;
            
            // 插入到目标位置
            allTasks.Insert(targetIndex, draggedTask);
            
            // 重新分配 SortOrder（从0开始递增）
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
            {
                orders.Add((tasks[i].Id, i));
            }
            _dbService.UpdateTaskOrder(orders);
        }

        // ========== 同步操作 ==========

        [RelayCommand]
        private async Task SyncAllAsync()
        {
            if (!AuthService.Instance.IsLoggedIn)
            {
                _messageService.ShowWarning("请先登录后再同步", "未登录");
                return;
            }

            IsSyncing = true;
            SyncStatusText = "正在同步...";

            try
            {
                var result = await _syncService.SyncAsync();
                
                if (result.Success)
                {
                    LastSyncTime = DateTime.Now;
                    SyncStatusText = $"同步完成：上传 {result.Uploaded} 条，下载 {result.Downloaded} 条";
                    
                    // 刷新本地数据
                    LoadData();
                }
                else
                {
                    SyncStatusText = $"同步失败：{result.Error}";
                    _messageService.ShowError($"同步失败：{result.Error}", "同步错误");
                }
            }
            catch (Exception ex)
            {
                SyncStatusText = $"同步出错：{ex.Message}";
                _messageService.ShowError($"同步出错：{ex.Message}", "同步错误");
            }
            finally
            {
                IsSyncing = false;
            }
        }

        [RelayCommand]
        private async Task UploadAsync()
        {
            if (!AuthService.Instance.IsLoggedIn)
            {
                _messageService.ShowWarning("请先登录后再上传", "未登录");
                return;
            }

            IsSyncing = true;
            SyncStatusText = "正在上传本地数据...";

            try
            {
                var uploaded = await _syncService.UploadLocalChangesAsync();
                LastSyncTime = DateTime.Now;
                SyncStatusText = $"上传完成：{uploaded} 条数据已上传";
                _messageService.ShowMessage($"成功上传 {uploaded} 条数据到云端", "上传完成");
            }
            catch (Exception ex)
            {
                SyncStatusText = $"上传出错：{ex.Message}";
                _messageService.ShowError($"上传出错：{ex.Message}", "上传错误");
            }
            finally
            {
                IsSyncing = false;
            }
        }

        [RelayCommand]
        private async Task DownloadAsync()
        {
            if (!AuthService.Instance.IsLoggedIn)
            {
                _messageService.ShowWarning("请先登录后再下载", "未登录");
                return;
            }

            IsSyncing = true;
            SyncStatusText = "正在从云端下载数据...";

            try
            {
                var downloaded = await _syncService.DownloadRemoteChangesAsync();
                LastSyncTime = DateTime.Now;
                SyncStatusText = $"下载完成：{downloaded} 条数据已下载";
                
                // 刷新本地数据
                LoadData();
                
                _messageService.ShowMessage($"成功从云端下载 {downloaded} 条数据", "下载完成");
            }
            catch (Exception ex)
            {
                SyncStatusText = $"下载出错：{ex.Message}";
                _messageService.ShowError($"下载出错：{ex.Message}", "下载错误");
            }
            finally
            {
                IsSyncing = false;
            }
        }

        public void Dispose()
        {
            // 取消事件订阅，防止内存泄漏
            DailyTasks.CollectionChanged -= OnTaskCollectionChanged;
            DeadlineTasks.CollectionChanged -= OnTaskCollectionChanged;
            HistoryTasks.CollectionChanged -= OnTaskCollectionChanged;
            CurrentTasks.CollectionChanged -= OnTaskCollectionChanged;
            
            // 注意：不 dispose 全局 DatabaseService 单例，它由 App 生命周期管理
        }
    }
}
