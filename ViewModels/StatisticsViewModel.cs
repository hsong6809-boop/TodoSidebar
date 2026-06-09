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
        private readonly TaskService _taskService;

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
            _taskService = new TaskService(dbService);
            LoadStatistics();
        }

        public void LoadStatistics()
        {
            var allTasks = _dbService.GetTasks();
            var today = DateTime.Today;

            // 总体统计
            TotalTasks = allTasks.Count;
            CompletedTasks = allTasks.Count(t => t.IsCompleted);
            PendingTasks = TotalTasks - CompletedTasks;
            CompletionRate = TotalTasks > 0 ? (double)CompletedTasks / TotalTasks : 0;

            // 今日统计
            var todayTasks = allTasks.Where(t => t.CreatedAt.Date == today).ToList();
            TodayTotal = todayTasks.Count;
            TodayCompleted = todayTasks.Count(t => t.IsCompleted);
            TodayCompletionRate = TodayTotal > 0 ? (double)TodayCompleted / TodayTotal : 0;

            // 过期任务
            OverdueTasks = allTasks.Count(t => 
                t.Type == TaskType.Deadline && 
                t.Deadline.HasValue && 
                t.Deadline.Value.Date < today && 
                !t.IsCompleted);

            // 高优先级任务
            HighPriorityTasks = allTasks.Count(t => 
                t.Priority == TaskPriority.High && !t.IsCompleted);

            // 连续完成天数
            StreakDays = CalculateStreakDays(allTasks);

            // 每日统计（最近7天）
            DailyStats = CalculateDailyStats(allTasks, 7);

            // 任务类型统计
            TaskTypeStats = CalculateTaskTypeStats(allTasks);
        }

        private int CalculateStreakDays(List<TaskItem> tasks)
        {
            int streak = 0;
            var date = DateTime.Today;

            while (true)
            {
                var dayTasks = tasks.Where(t => t.CreatedAt.Date == date).ToList();
                if (dayTasks.Count == 0) break;

                var allCompleted = dayTasks.All(t => t.IsCompleted);
                if (!allCompleted) break;

                streak++;
                date = date.AddDays(-1);
            }

            return streak;
        }

        private List<DailyStats> CalculateDailyStats(List<TaskItem> tasks, int days)
        {
            var stats = new List<DailyStats>();

            for (int i = days - 1; i >= 0; i--)
            {
                var date = DateTime.Today.AddDays(-i);
                var dayTasks = tasks.Where(t => t.CreatedAt.Date == date).ToList();

                stats.Add(new DailyStats
                {
                    Date = date,
                    TotalTasks = dayTasks.Count,
                    CompletedTasks = dayTasks.Count(t => t.IsCompleted),
                    CompletionRate = dayTasks.Count > 0 ? (double)dayTasks.Count(t => t.IsCompleted) / dayTasks.Count : 0
                });
            }

            return stats;
        }

        private List<TaskTypeStats> CalculateTaskTypeStats(List<TaskItem> tasks)
        {
            return new List<TaskTypeStats>
            {
                new TaskTypeStats
                {
                    Type = "每日任务",
                    Count = tasks.Count(t => t.Type == TaskType.Daily),
                    Completed = tasks.Count(t => t.Type == TaskType.Daily && t.IsCompleted),
                    Color = "#5B5FE9"
                },
                new TaskTypeStats
                {
                    Type = "截止任务",
                    Count = tasks.Count(t => t.Type == TaskType.Deadline),
                    Completed = tasks.Count(t => t.Type == TaskType.Deadline && t.IsCompleted),
                    Color = "#FF5A5A"
                }
            };
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
