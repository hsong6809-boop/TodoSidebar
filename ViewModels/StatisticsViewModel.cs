using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
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

        [ObservableProperty]
        private List<DailyStats> _dailyStats = new();

        [ObservableProperty]
        private List<TaskTypeStats> _taskTypeStats = new();

        public StatisticsViewModel(DatabaseService dbService)
        {
            _dbService = dbService;
            LoadStatistics();
        }

        public void LoadStatistics()
        {
            var allTasks = _dbService.GetTasks();
            var today = DateTime.Today;
            var dailyCompletionRecords = _dbService.GetDailyCompletionRecords(7);

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
            var todayStr = today.ToString("yyyy-MM-dd");
            var todayCompletedDaily = dailyCompletionRecords.TryGetValue(todayStr, out var todaySet)
                ? todaySet.Count : 0;
            TodayCompleted = todayCompletedDaily + todayCompletedDeadlines;
            TodayCompletionRate = TodayTotal > 0 ? (double)TodayCompleted / TodayTotal : 0;

            // 连续完成天数（M24：独立查询窗口 + 与连击结算口径对齐）
            StreakDays = CalculateStreakDays();

            // 每日统计（最近7天）
            DailyStats = CalculateDailyStats(dailyCompletionRecords, 7, dailyCount);

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
            var todayCompleted = records.TryGetValue(today.ToString("yyyy-MM-dd"), out var todaySet)
                ? todaySet.Count : 0;
            if (todayCompleted < todayCount)
                start = today.AddDays(-1);

            var earliest = today.AddDays(-365);
            int streak = 0;
            var date = start;
            while (date >= earliest)
            {
                var dateStr = date.ToString("yyyy-MM-dd");
                var completed = records.TryGetValue(dateStr, out var set) ? set.Count : 0;
                var expected = _dbService.GetDailyTaskCountAsOf(dateStr);
                if (expected == 0 || completed < expected)
                    break;

                streak++;
                date = date.AddDays(-1);
            }

            return streak;
        }

        private List<DailyStats> CalculateDailyStats(
            Dictionary<string, HashSet<int>> dailyCompletionRecords, int days, int dailyTaskCount)
        {
            var stats = new List<DailyStats>();

            for (int i = days - 1; i >= 0; i--)
            {
                var date = DateTime.Today.AddDays(-i);
                var dateStr = date.ToString("yyyy-MM-dd");
                var completedCount = dailyCompletionRecords.TryGetValue(dateStr, out var dateSet)
                    ? dateSet.Count : 0;

                stats.Add(new DailyStats
                {
                    Date = date,
                    TotalTasks = dailyTaskCount,
                    CompletedTasks = completedCount,
                    CompletionRate = dailyTaskCount > 0 ? (double)completedCount / dailyTaskCount : 0
                });
            }

            return stats;
        }
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
