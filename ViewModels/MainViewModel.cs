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

        // ===== 升级系统 =====
        [ObservableProperty]
        private int _level = 1;

        [ObservableProperty]
        private string _levelTitle = "初出茅庐";

        [ObservableProperty]
        private double _levelProgress;

        [ObservableProperty]
        private string _levelProgressText = "0/100 XP";

        [ObservableProperty]
        private string _levelDisplay = "Lv.1 初出茅庐";

        /// <summary>连击展示文本，如 "🔥 x7"（无连击为空）</summary>
        [ObservableProperty]
        private string _comboDisplay = "";

        // ===== V2 侧边栏驾驶舱：今日进度 =====
        [ObservableProperty]
        private double _todayProgressRate;

        [ObservableProperty]
        private string _todayDoneText = "0 / 0";

        /// <summary>集合变化时重算今日进度（已完成 / 已完成+待办）。</summary>
        private void RefreshTodayProgress()
        {
            var done = TodayCompletedTasks.Count;
            var total = done + CurrentTasks.Count;
            TodayProgressRate = total == 0 ? 0 : (double)done / total;
            TodayDoneText = $"{done} / {total}";
        }

        // ===== V2 侧边栏「接下来」行动卡 =====
        [ObservableProperty]
        private string _nextTaskTitle = "暂无待办";

        /// <summary>取当前待办第一项标题。</summary>
        public void RefreshNextTask()
            => NextTaskTitle = CurrentTasks.FirstOrDefault()?.Title ?? "暂无待办";

        /// <summary>升级事件（窗口订阅显示横幅与粒子）</summary>
        public event EventHandler<LevelUpEventArgs>? LevelUpOccurred;

        /// <summary>成就解锁事件（窗口订阅显示横幅）</summary>
        public event EventHandler<AchievementUnlockedEventArgs>? AchievementUnlockedOccurred;

        /// <summary>连击结算事件（窗口订阅刷新显示）</summary>
        public event EventHandler<ComboSettledEventArgs>? ComboSettledOccurred;

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
            _taskService = new TaskService(_dbService);
            _messageService = MessageService.Instance;

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

            // 升级系统：初始化等级信息并订阅升级事件
            LevelService.Instance.LevelUp += OnLevelUp;
            LevelService.Instance.XpChanged += OnXpChanged;
            LevelService.Instance.ComboSettled += OnComboSettled;
            AchievementService.Instance.AchievementUnlocked += OnAchievementUnlocked;
            LoadLevelInfo();
            UpdateComboDisplay();

            // S9 修复：启动时补结算错过的连击（应用未在午夜运行的场景）
            try { LevelService.Instance.SettleCombo(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Startup combo settle error: {ex.Message}"); }

            // 成就补检（启动时检查已达标未解锁的徽章）
            AchievementService.Instance.CheckAll();

            LoadData();
        }

        private void OnComboSettled(object? sender, ComboSettledEventArgs e)
        {
            UpdateComboDisplay();
            ComboSettledOccurred?.Invoke(this, e);
        }

        private void OnAchievementUnlocked(object? sender, AchievementUnlockedEventArgs e)
        {
            AchievementUnlockedOccurred?.Invoke(this, e);
        }

        private void UpdateComboDisplay()
        {
            var combo = LevelService.Instance.GetGrowth().ComboDays;
            ComboDisplay = combo > 0 ? $"🔥 x{combo}" : "";
        }

        private void LoadLevelInfo()
        {
            try
            {
                var growth = LevelService.Instance.GetGrowth();
                var info = LevelService.Instance.GetLevelInfo(growth);
                Level = info.Level;
                LevelTitle = info.Title;
                LevelProgress = info.Progress;
                LevelProgressText = info.ProgressText;
                LevelDisplay = LevelService.FormatLevelDisplay(info.Level, info.Title);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadLevelInfo error: {ex.Message}");
            }
        }

        private void OnLevelUp(object? sender, LevelUpEventArgs e)
        {
            // 刷新等级显示（可能跨线程，但升级事件由 UI 线程任务操作触发，此处直接更新）
            Level = e.NewLevel;
            LevelTitle = e.NewTitle;
            LevelProgress = 0;
            LevelProgressText = $"0/{LevelService.XpForNextLevel(e.NewLevel)} XP";
            LevelDisplay = LevelService.FormatLevelDisplay(e.NewLevel, e.NewTitle);
            LevelUpOccurred?.Invoke(this, e);
        }

        /// <summary>
        /// 经验变更（未升级）：只刷新经验进度条。
        /// </summary>
        private void OnXpChanged(object? sender, EventArgs e)
        {
            var growth = LevelService.Instance.GetGrowth();
            Level = growth.Level;
            LevelTitle = growth.Title;
            LevelProgress = growth.Xp / (double)LevelService.XpForNextLevel(growth.Level);
            LevelProgressText = $"{growth.Xp}/{LevelService.XpForNextLevel(growth.Level)} XP";
            LevelDisplay = LevelService.FormatLevelDisplay(growth.Level, growth.Title);
        }

        private void ScheduleMidnightRefresh()
        {
            var now = DateTime.Now;
            var midnight = now.Date.AddDays(1);
            // 下限 1 秒保护：时钟调整/闰秒等场景下 Interval 不能为负
            var msUntilMidnight = Math.Max(1000, (midnight - now).TotalMilliseconds);

            if (_midnightTimer == null)
            {
                _midnightTimer = new DispatcherTimer();
                _midnightTimer.Tick += (s, e) =>
                {
                    _midnightTimer?.Stop();
                    OnMidnightRollover();
                    ScheduleMidnightRefresh();
                };
            }

            _midnightTimer.Interval = TimeSpan.FromMilliseconds(msUntilMidnight);
            _midnightTimer.Start();
        }

        /// <summary>
        /// 每日零点结算：连击结算（全清+1 / 断连清零）→ 成就检查 → 刷新数据。
        /// </summary>
        private void OnMidnightRollover()
        {
            try
            {
                LevelService.Instance.SettleCombo();
                UpdateComboDisplay();
                AchievementService.Instance.CheckAll();
                LoadData();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Midnight rollover error: {ex.Message}");
            }
        }

        private void OnTaskCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (sender == DailyTasks) OnPropertyChanged(nameof(DailyTasksCount));
            else if (sender == DeadlineTasks) OnPropertyChanged(nameof(DeadlineTasksCount));
            else if (sender == TodayCompletedTasks) OnPropertyChanged(nameof(TodayCompletedTasksCount));
            else if (sender == HistoryTasks) OnPropertyChanged(nameof(HistoryTasksCount));
            else if (sender == CurrentTasks) OnPropertyChanged(nameof(CurrentTasksCount));

            // V2 侧边栏驾驶舱：今日进度环与计数联动
            if (sender == TodayCompletedTasks || sender == CurrentTasks)
            {
                RefreshTodayProgress();
                RefreshNextTask();
            }
        }

        /// <summary>
        /// 重新加载全部任务数据（公共入口，供番茄钟/外部组件刷新用）。
        /// </summary>
        public void LoadData()
        {
            LoadDailyTasks();
            LoadDeadlineTasks();
            LoadTodayCompletedTasks();
            LoadHistoryTasks();
            LoadCurrentTasks();
            StatisticsViewModel.LoadStatistics();

            // M39 修复：同步完成后刷新等级/连击显示——云端档案合并进本地库后，
            // 原实现只重载任务列表，等级要重启应用才会变对，造成"等级没同步"的观感
            LoadLevelInfo();
            UpdateComboDisplay();
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

        // ===== V5.1：Composer 自然语言解析 =====

        /// <summary>解析输入框当前内容（纯静态函数，供提交前取用；无副作用）。</summary>
        public ParsedTask ParseComposerInput() => NaturalLanguageParser.Parse(NewTaskTitle);

        /// <summary>解析预览文本；为空时 Composer 预览条隐藏。形如 "📅 明天 15:00 · 高优先级 · #工作"。</summary>
        [ObservableProperty]
        private string _composerPreview = string.Empty;

        /// <summary>Composer 自然语言解析出的待写入标签（落库后清空）。</summary>
        public List<string>? PendingTags { get; set; }

        partial void OnNewTaskTitleChanged(string value) => RefreshComposerPreview();

        /// <summary>输入变化时刷新解析预览（纯计算，不落库）。</summary>
        private void RefreshComposerPreview()
        {
            var p = NaturalLanguageParser.Parse(NewTaskTitle);
            var parts = new List<string>();
            if (p.DueDate.HasValue)
                parts.Add(ComposerDateChip(p.DueDate.Value));
            if (p.Priority.HasValue)
                parts.Add(p.Priority.Value switch
                {
                    TaskPriority.High => "高优先级",
                    TaskPriority.Low => "低优先级",
                    _ => "中优先级"
                });
            parts.AddRange(p.Tags.Select(t => "#" + t));
            ComposerPreview = string.Join("  ·  ", parts);
        }

        private static string ComposerDateChip(DateTime d)
        {
            var today = DateTime.Today;
            var day = d.Date == today ? "今天"
                    : d.Date == today.AddDays(1) ? "明天"
                    : d.ToString("MM月dd日");
            return d.TimeOfDay == TimeSpan.Zero ? day : $"{day} {d:HH:mm}";
        }

        /// <summary>新建任务落库后补写标签并清空暂存。</summary>
        private void ApplyPendingTags(TaskItem task)
        {
            if (PendingTags is { Count: > 0 })
            {
                task.Tags = string.Join(",", PendingTags);
                _dbService.UpdateTask(task);
            }
            PendingTags = null;
        }

        // ========== 任务 CRUD 命令 ==========

        [RelayCommand]
        private void AddDailyTask()
        {
            if (string.IsNullOrWhiteSpace(NewTaskTitle)) return;

            // V5.1：解析出的干净标题/标签/优先级在落库时生效（每日任务不含截止时间）
            var parsed = NaturalLanguageParser.Parse(NewTaskTitle);
            if (parsed.Title.Length > 0) NewTaskTitle = parsed.Title;
            if (parsed.Tags.Count > 0) PendingTags = parsed.Tags;
            var priority = parsed.Priority ?? NewTaskPriority;

            var task = _taskService.AddTask(NewTaskTitle, TaskType.Daily, null, priority);
            ApplyPendingTags(task);
            NewTaskTitle = string.Empty;
            NewTaskPriority = TaskPriority.Medium;
            LoadDailyTasks();
            LoadCurrentTasks();
        }

        [RelayCommand]
        private void AddDeadlineTask()
        {
            if (string.IsNullOrWhiteSpace(NewTaskTitle)) return;

            // V5.1：把 "明天下午3点 交周报" 解析出的时间/优先级/标签并入提交
            var parsed = NaturalLanguageParser.Parse(NewTaskTitle);
            if (parsed.Title.Length > 0) NewTaskTitle = parsed.Title;
            if (parsed.Tags.Count > 0) PendingTags = parsed.Tags;
            var deadline = NewTaskDeadline ?? parsed.DueDate;
            var priority = parsed.Priority ?? NewTaskPriority;

            if (deadline.HasValue && deadline.Value.Date < DateTime.Today)
            {
                _messageService.ShowWarning("截止日期不能早于今天！", "日期错误");
                return;
            }

            var task = _taskService.AddTask(NewTaskTitle, TaskType.Deadline, deadline, priority);
            ApplyPendingTags(task);
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
            // M22 修复：全量刷新，保证 FullWindow 的 Daily/Deadline 集合同步更新
            LoadData();
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
            // M22 修复：全量刷新，保证 FullWindow 的 Daily/Deadline 集合同步更新
            LoadData();
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
                // M22 修复：全量刷新，保证 FullWindow 的 Daily/Deadline 集合同步更新
                LoadData();
            }
        }

        /// <summary>
        /// 刷新子任务相关 UI 绑定（在 TaskItem 实例上触发通知，确保 UI 收到）
        /// </summary>
        private void RefreshTaskProperties(TaskItem task)
        {
            if (task == null) return;
            // SubTasksList/SubTasksProgressText/HasSubTasks 是 TaskItem 的属性，须在 task 上触发通知
            task.NotifySubTaskPropertiesChanged();
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

            // 排序键与 GetCurrentTasks/数据库查询保持一致（SortOrder, CreatedAt DESC）
            var allTasks = CurrentTasks.OrderBy(t => t.SortOrder).ThenByDescending(t => t.CreatedAt).ToList();
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

        public void Dispose()
        {
            LevelService.Instance.LevelUp -= OnLevelUp;
            LevelService.Instance.XpChanged -= OnXpChanged;
            LevelService.Instance.ComboSettled -= OnComboSettled;
            AchievementService.Instance.AchievementUnlocked -= OnAchievementUnlocked;
            DailyTasks.CollectionChanged -= OnTaskCollectionChanged;
            DeadlineTasks.CollectionChanged -= OnTaskCollectionChanged;
            HistoryTasks.CollectionChanged -= OnTaskCollectionChanged;
            CurrentTasks.CollectionChanged -= OnTaskCollectionChanged;
            TodayCompletedTasks.CollectionChanged -= OnTaskCollectionChanged;
            SyncViewModel.OnSyncCompleted = null;
            _midnightTimer?.Stop();
            _midnightTimer = null;
        }
    }
}
