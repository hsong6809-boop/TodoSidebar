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
            int dailyCount = 0, dailyCompleted = 0, deadlineCount = 0, deadlineCompleted = 0;
            
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
            TodayTotal = dailyCount + deadlineCount;
            var todayStr = today.ToString("yyyy-MM-dd");
            var todayCompletedDaily = dailyCompletionRecords.ContainsKey(todayStr) 
                ? dailyCompletionRecords[todayStr].Count : 0;
            TodayCompleted = todayCompletedDaily + deadlineCompleted;
            TodayCompletionRate = TodayTotal > 0 ? (double)TodayCompleted / TodayTotal : 0;

            // 连续完成天数
            StreakDays = CalculateStreakDays(dailyCompletionRecords);

            // 每日统计（最近7天）
            DailyStats = CalculateDailyStats(dailyCompletionRecords, 7, dailyCount);

            // 任务类型统计
            TaskTypeStats = new List<TaskTypeStats>
            {
                new TaskTypeStats
                {
                    Type = "每日任务",
                    Count = dailyCount,
                    Completed = dailyCompletionRecords.ContainsKey(todayStr) 
                        ? dailyCompletionRecords[todayStr].Count : 0,
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

        private int CalculateStreakDays(Dictionary<string, HashSet<int>> dailyCompletionRecords)
        {
            int streak = 0;
            var date = DateTime.Today;
            var dailyTaskCount = _dbService.GetDailyTaskCount();

            // 没有每日任务则无连续天数
            if (dailyTaskCount == 0) return 0;

            while (true)
            {
                var dateStr = date.ToString("yyyy-MM-dd");
                if (!dailyCompletionRecords.ContainsKey(dateStr))
                    break;
                
                // 当天完成数 >= 每日任务总数才算全部完成
                if (dailyCompletionRecords[dateStr].Count < dailyTaskCount)
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
                var completedCount = dailyCompletionRecords.ContainsKey(dateStr) 
                    ? dailyCompletionRecords[dateStr].Count : 0;

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
