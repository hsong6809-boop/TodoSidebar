using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TodoSidebar.Models;
using TodoSidebar.Services;

namespace TodoSidebar.ViewModels
{
    public partial class StatisticsViewModel : ObservableObject
    {
        private readonly DatabaseService _dbService;

        [ObservableProperty]
        private int _totalTasks;

        [ObservableProperty]
        private int _completedTasks;

        [ObservableProperty]
        private int _pendingTasks;

        [ObservableProperty]
        private double _completionRate;

        [ObservableProperty]
        private int _todayCompleted;

        [ObservableProperty]
        private int _todayTotal;

        [ObservableProperty]
        private double _todayCompletionRate;

        [ObservableProperty]
        private int _overdueTasks;

        [ObservableProperty]
        private int _highPriorityTasks;

        [ObservableProperty]
        private int _streakDays;

        /// <summary>今日打字量展示文本（R61 输入统计；未开启时为引导文案）。</summary>
        [ObservableProperty]
        private string _todayTypingText = "—";

        // ===== R61 秒级实时刷新 =====
        private System.Windows.Threading.DispatcherTimer? _typingTimer;
        private string? _lastTypingText;

        /// <summary>
        /// 从服务内存读今日实时总数（基线+当前计数，不落库），数字有变化才通知 UI。
        /// 未开启功能时显示引导文案并停表。
        /// </summary>
        private void UpdateTypingText()
        {
            try
            {
                if (_dbService.GetSetting("TypingStatsEnabled") != "true")
                {
                    StopTypingTimer();
                    SetTypingText("未开启 · 可在设置中打开");
                    return;
                }

                StartTypingTimer();
                var (k, w) = TypingStatsService.Instance.GetLiveTotals();
                SetTypingText(k == 0 && w == 0
                    ? "今天还没有输入"
                    : $"约 {w.ToString("N0", CultureInfo.InvariantCulture)} 字 · {k.ToString("N0", CultureInfo.InvariantCulture)} 键");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateTypingText error: {ex.Message}");
            }
        }

        private void SetTypingText(string text)
        {
            if (_lastTypingText == text) return;   // 无变化不打扰绑定
            _lastTypingText = text;
            TodayTypingText = text;
        }

        private void StartTypingTimer()
        {
            if (_typingTimer != null) return;
            _typingTimer = new System.Windows.Threading.DispatcherTimer(
                System.Windows.Threading.DispatcherPriority.Background)
            { Interval = TimeSpan.FromSeconds(1) };
            _typingTimer.Tick += (_, _) => UpdateTypingText();
            _typingTimer.Start();
        }

        private void StopTypingTimer()
        {
            _typingTimer?.Stop();
            _typingTimer = null;
        }

        /// <summary>外部触发立即刷新（设置页开关切换后调用）；按当前开关联动启停。</summary>
        public void RefreshTyping() => UpdateTypingText();

        /// <summary>登出重建 ViewModel 时释放定时器（进程退出场景由调度器随线程销毁兜底）。</summary>
        public void ShutdownTypingTimer() => StopTypingTimer();

        [ObservableProperty]
        private List<DailyStats> _dailyStats = new();

        [ObservableProperty]
        private List<TaskTypeStats> _taskTypeStats = new();

        // ===== v5.3 年度热力图 =====

        /// <summary>热力图展示年份（默认今年）。</summary>
        [ObservableProperty]
        private int _heatmapYear = DateTime.Today.Year;

        /// <summary>热力图格子序列（含首周前置空白），供 WrapPanel 纵向换列渲染。</summary>
        [ObservableProperty]
        private List<HeatmapDay> _yearHeatmap = new();

        /// <summary>热力图年度汇总文本。</summary>
        [ObservableProperty]
        private string _heatmapSummaryText = "";

        /// <summary>图例色阶样例（L0~L4）。</summary>
        public List<HeatmapDay> HeatmapLegend { get; } = new()
        {
            new(null, 0), new(null, 1), new(null, 3), new(null, 6), new(null, 10)
        };

        public StatisticsViewModel(DatabaseService dbService)
        {
            _dbService = dbService;
            LoadStatistics();
            LoadHeatmap(DateTime.Today.Year);
        }

        /// <summary>v5.3：加载指定年份的完成热力图数据。</summary>
        public void LoadHeatmap(int year)
        {
            try
            {
                HeatmapYear = year;
                var counts = _dbService.GetHeatmapCounts(
                    new DateTime(year, 1, 1), new DateTime(year, 12, 31));

                var list = new List<HeatmapDay>();
                // 首周对齐：1 月 1 日是周几，就先补几个空白格（周一为第一行）
                var jan1 = new DateTime(year, 1, 1);
                var leadDays = ((int)jan1.DayOfWeek + 6) % 7; // 周一=0 … 周日=6
                for (int i = 0; i < leadDays; i++)
                    list.Add(HeatmapDay.Blank);

                int total = 0, activeDays = 0;
                var day = jan1;
                while (day.Year == year)
                {
                    // R39 修复（审查 M3/M12）：日期键统一 InvariantCulture，
                    // 非公历默认日历文化下 ToString 会输出圣历年等键值、与库内数据永不匹配
                    var key = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    var count = counts.TryGetValue(key, out var c) ? c : 0;
                    total += count;
                    if (count > 0) activeDays++;
                    list.Add(new HeatmapDay(day, count));
                    day = day.AddDays(1);
                }

                YearHeatmap = list;
                HeatmapSummaryText = $"{year} 年共完成 {total} 次 · 活跃 {activeDays} 天";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadHeatmap error: {ex.Message}");
            }
        }

        [RelayCommand]
        private void PrevHeatmapYear() => LoadHeatmap(HeatmapYear - 1);

        [RelayCommand]
        private void NextHeatmapYear()
        {
            if (HeatmapYear < DateTime.Today.Year)
                LoadHeatmap(HeatmapYear + 1);
        }

        public void LoadStatistics()
        {
            var allTasks = _dbService.GetTasks();
            var today = DateTime.Today;
            var dailyCompletionRecords = _dbService.GetDailyCompletionRecords(7);

            // R61「输入统计」：今日打字量（秒级实时，见 UpdateTypingText）
            UpdateTypingText();

            // 单次遍历计算多个统计指标
            int total = 0, completed = 0, overdue = 0, highPrio = 0;
            int dailyCount = 0, deadlineCount = 0, deadlineCompleted = 0;
            
            foreach (var task in allTasks)
            {
                total++;
                if (task.IsCompleted) completed++;
                if (task.Priority == TaskPriority.High && !task.IsCompleted) highPrio++;
                if (task.Type == TaskType.Deadline && task.Deadline.HasValue 
                    && task.Deadline.Value.Date < today && !task.IsCompleted) overdue++;
                
                if (task.Type == TaskType.Daily) dailyCount++;
                else if (task.Type == TaskType.Deadline)
                {
                    deadlineCount++;
                    if (task.IsCompleted) deadlineCompleted++;
                }
            }

            TotalTasks = total;
            CompletedTasks = completed;
            PendingTasks = total - completed;
            CompletionRate = total > 0 ? (double)completed / total : 0;
            OverdueTasks = overdue;
            HighPriorityTasks = highPrio;

            // 今日统计（结合 DailyTaskCompletion 表）
            // 截止任务只统计今天完成的，避免把全部历史截止任务算进今天
            var todayCompletedDeadlines = _dbService.GetCompletedTasks(today, today.AddDays(1))
                .Count(t => t.Type == TaskType.Deadline);
            // 分母：每日任务总数 + 尚未完成且未过期的截止任务数
            var pendingValidDeadlines = allTasks.Count(t => t.Type == TaskType.Deadline
                && t.Deadline.HasValue && t.Deadline.Value.Date >= today && !t.IsCompleted);
            TodayTotal = dailyCount + pendingValidDeadlines;
            // R39（审查 M3/M12）：与写入端(TaskService L7)对齐 InvariantCulture
            var todayStr = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var todayCompletedDaily = dailyCompletionRecords.TryGetValue(todayStr, out var todaySet)
                ? todaySet.Count : 0;
            TodayCompleted = todayCompletedDaily + todayCompletedDeadlines;
            TodayCompletionRate = TodayTotal > 0 ? (double)TodayCompleted / TodayTotal : 0;

            // 连续完成天数（M24：独立查询窗口 + 与连击结算口径对齐）
            StreakDays = CalculateStreakDays();

            // 每日统计（最近7天）。
            // R38 修复（审查 L4/L9）：历史天的分母改用"当天时点的每日任务数"序列
            // （一次拉取、本地聚合），不再错用今天的任务数，也不再逐日打库
            var asOfCounts = BuildAsOfCountSeries(today, 7);
            DailyStats = CalculateDailyStats(dailyCompletionRecords, 7, asOfCounts);

            // 任务类型统计
            TaskTypeStats = new List<TaskTypeStats>
            {
                new TaskTypeStats
                {
                    Type = "每日任务",
                    Count = dailyCount,
                    Completed = todayCompletedDaily,
                    Color = "#5B5FE9"
                },
                new TaskTypeStats
                {
                    Type = "截止任务",
                    Count = deadlineCount,
                    Completed = deadlineCompleted,
                    Color = "#FF5A5A"
                }
            };
        }

        private int CalculateStreakDays()
        {
            // M24 修复：
            // ① 独立拉取 365 天完成记录——原实现复用最近 7 天数据，streak 恒 ≤ 7；
            // ② 今天未全清时从昨天起算——与午夜连击结算口径一致，白天未全清不再恒显示 0；
            // ③ 历史某天是否全清用该日期时点的任务数判定（GetDailyTaskCountAsOf），
            //    不再用当前任务数回溯历史
            var records = _dbService.GetDailyCompletionRecords(365);
            var today = DateTime.Today;
            var todayCount = _dbService.GetDailyTaskCount();
            if (todayCount == 0) return 0;

            var start = today;
            var todayKey = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); // R39
            var todayCompleted = records.TryGetValue(todayKey, out var todaySet)
                ? todaySet.Count : 0;
            if (todayCompleted < todayCount)
                start = today.AddDays(-1);

            // R38 修复（审查 L9）：一次拉取创建日期、本地聚合"每天时点任务数"，
            // 取代循环内逐日 GetDailyTaskCountAsOf（最坏 ~365 次加锁查询、UI 线程卡顿）
            var asOfCounts = BuildAsOfCountSeries(today, 366);

            var earliest = today.AddDays(-365);
            int streak = 0;
            var date = start;
            while (date >= earliest)
            {
                var dateStr = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); // R39
                var completed = records.TryGetValue(dateStr, out var set) ? set.Count : 0;
                var expected = asOfCounts.TryGetValue(dateStr, out var n) ? n : 0;
                if (expected == 0 || completed < expected)
                    break;

                streak++;
                date = date.AddDays(-1);
            }

            return streak;
        }

        /// <summary>
        /// R38：构建 endDate 起往前 days 天的"每天时点每日任务数"序列（含 endDate 当天）。
        /// 口径与 GetDailyTaskCountAsOf 一致（CreatedAt 本地日期 ≤ 该天，存活行）。
        /// </summary>
        private Dictionary<string, int> BuildAsOfCountSeries(DateTime endDate, int days)
        {
            var createdDates = _dbService.GetDailyTaskCreatedDates();
            var sorted = createdDates.Select(d => d.Date).OrderBy(d => d).ToList();

            var series = new Dictionary<string, int>(days);
            int idx = 0;
            for (int i = days - 1; i >= 0; i--)
            {
                var day = endDate.AddDays(-i);
                while (idx < sorted.Count && sorted[idx] <= day) idx++;
                series[day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)] = idx;
            }
            return series;
        }

        private List<DailyStats> CalculateDailyStats(
            Dictionary<string, HashSet<int>> dailyCompletionRecords, int days,
            Dictionary<string, int> asOfTaskCounts)
        {
            var stats = new List<DailyStats>();

            for (int i = days - 1; i >= 0; i--)
            {
                var date = DateTime.Today.AddDays(-i);
                // R39（审查 M3/M12）：InvariantCulture
                var dateStr = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var completedCount = dailyCompletionRecords.TryGetValue(dateStr, out var dateSet)
                    ? dateSet.Count : 0;
                // R38 修复（审查 L4）：历史分母用"当天时点"的任务数，
                // 不再用今天的数量回溯历史（本周增删过任务时完成率被系统性抬高/压低）
                var totalForDay = asOfTaskCounts.TryGetValue(dateStr, out var t) ? t : 0;

                stats.Add(new DailyStats
                {
                    Date = date,
                    TotalTasks = totalForDay,
                    CompletedTasks = completedCount,
                    CompletionRate = totalForDay > 0 ? (double)completedCount / totalForDay : 0
                });
            }

            return stats;
        }
    }

    /// <summary>v5.3 热力图单元格。</summary>
    public class HeatmapDay
    {
        /// <summary>色阶 0（无）~4（最密）；IsBlank 时无意义。</summary>
        public const int MaxLevel = 4;

        public DateTime? Date { get; }
        public int Count { get; }
        public bool IsBlank { get; }
        public int Level { get; }

        public static HeatmapDay Blank { get; } = new(null, 0, isBlank: true);

        public HeatmapDay(DateTime? date, int count, bool isBlank = false)
        {
            Date = date;
            Count = count;
            IsBlank = isBlank;
            Level = count switch
            {
                <= 0 => 0,
                <= 2 => 1,
                <= 5 => 2,
                <= 9 => 3,
                _ => 4
            };
        }

        public string ToolTip => Date.HasValue
            ? $"{Date.Value:M月d日} · 完成 {Count} 次"
            : "";
    }

    public class DailyStats
    {
        public DateTime Date { get; set; }
        public int TotalTasks { get; set; }
        public int CompletedTasks { get; set; }
        public double CompletionRate { get; set; }
        public string DateLabel => Date.ToString("MM/dd");

        /// <summary>V2.1：点阵状态（none/done/today），供连击卡 7 日点阵配色。</summary>
        public bool IsToday => Date.Date == DateTime.Today;
        public string DotState => CompletedTasks > 0 ? (IsToday ? "today" : "done") : "none";

        /// <summary>V2 侧边栏本周概览：星期单字。</summary>
        public string WeekdayChar => "日一二三四五六"[(int)Date.DayOfWeek].ToString();
        /// <summary>V2 侧边栏本周概览：几号。</summary>
        public int DayNumber => Date.Day;
    }

    public class TaskTypeStats
    {
        public string Type { get; set; } = "";
        public int Count { get; set; }
        public int Completed { get; set; }
        public string Color { get; set; } = "";
        public double CompletionRate => Count > 0 ? (double)Completed / Count : 0;
    }
}
