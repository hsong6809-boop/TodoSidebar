using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace TodoSidebar.Models
{
    public enum TaskType
    {
        Daily,      // 每日任务
        Deadline,   // 截止日期任务
    }

    public enum TaskPriority
    {
        Low,        // 低优先级
        Medium,     // 中优先级
        High        // 高优先级
    }

    public class TaskItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public TaskType Type { get; set; }
        public TaskPriority Priority { get; set; } = TaskPriority.Medium;
        
        private bool _isCompleted;
        public bool IsCompleted
        {
            get => _isCompleted;
            set { _isCompleted = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 今日是否已完成（每日任务专用，运行时计算，不持久化）
        /// </summary>
        private bool _isTodayCompleted;
        public bool IsTodayCompleted
        {
            get => _isTodayCompleted;
            set { _isTodayCompleted = value; OnPropertyChanged(); }
        }
        
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? Deadline { get; set; }
        
        private DateTime? _completedAt;
        public DateTime? CompletedAt
        {
            get => _completedAt;
            set { _completedAt = value; OnPropertyChanged(); }
        }
        

        public string? Description { get; set; }
        public string? Tags { get; set; }
        public int SortOrder { get; set; }
        public int? EstimatedMinutes { get; set; }
        public int? ActualMinutes { get; set; }

        // 同步相关字段
        /// <summary>
        /// Supabase 中的 UUID，用于关联本地和远程任务
        /// </summary>
        public string? SyncId { get; set; }
        
        /// <summary>
        /// 本地记录是否已修改但未同步
        /// </summary>
        public bool IsDirty { get; set; } = false;
        
        /// <summary>
        /// 最后同步时间
        /// </summary>
        public DateTime? LastSyncedAt { get; set; }
        
        /// <summary>
        /// 是否已软删除
        /// </summary>
        public bool IsDeleted { get; set; } = false;

        private string? _subTasksJson;
        public string? SubTasksJson
        {
            get => _subTasksJson;
            set
            {
                if (_subTasksJson != value)
                {
                    _subTasksJson = value;
                    _cachedSubTasks = null; // 清除缓存
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SubTasksList));
                    OnPropertyChanged(nameof(SubTasksProgressText));
                    OnPropertyChanged(nameof(SubTasksCount));
                    OnPropertyChanged(nameof(HasSubTasks));
                    OnPropertyChanged(nameof(AllSubTasksCompleted));
                }
            }
        }
        
        // 优先级颜色
        public string PriorityColor => Priority switch
        {
            TaskPriority.High => "#EF4444",   // Red-500
            TaskPriority.Medium => "#F59E0B",  // Amber-500
            TaskPriority.Low => "#10B981",     // Emerald-500
            _ => "#F59E0B"
        };
        
        // 任务类型颜色
        public string TypeColor => Type switch
        {
            TaskType.Daily => "#6366F1",     // Indigo-500
            TaskType.Deadline => "#EF4444",  // Red-500
            _ => "#6366F1"
        };

        // 类型文本
        public string TypeText => Type switch
        {
            TaskType.Daily => "每日",
            TaskType.Deadline => "截止",
            _ => "未知"
        };
        
        // 优先级图标
        public string PriorityIcon => Priority switch
        {
            TaskPriority.High => "🔴",
            TaskPriority.Medium => "🟡",
            TaskPriority.Low => "🟢",
            _ => "⚪"
        };
        
        // 截止日期紧急程度
        public string DeadlineUrgency
        {
            get
            {
                if (Deadline == null) return "";
                var timeLeft = Deadline.Value - DateTime.Now;
                
                if (timeLeft.TotalDays < 0)
                    return "已过期";
                else if (timeLeft.TotalHours < 1)
                    return $"{(int)timeLeft.TotalMinutes}分钟后";
                else if (timeLeft.TotalHours < 24)
                    return $"{(int)timeLeft.TotalHours}小时后";
                else
                    return $"{(int)timeLeft.TotalDays}天后";
            }
        }
        
        // 标签列表
        public List<string> TagList
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Tags))
                    return new List<string>();
                return new List<string>(Tags.Split(',', StringSplitOptions.RemoveEmptyEntries));
            }
        }
        
        // 子任务列表（从JSON解析，带缓存）
        private List<SubTask>? _cachedSubTasks;
        private string? _lastSubTasksJson;
        
        public List<SubTask> SubTasksList
        {
            get
            {
                if (_cachedSubTasks == null || SubTasksJson != _lastSubTasksJson)
                {
                    _cachedSubTasks = SubTaskHelper.ParseSubTasks(SubTasksJson);
                    _lastSubTasksJson = SubTasksJson;
                }
                return _cachedSubTasks;
            }
        }
        
        // 子任务进度文本（如 "2/5"）
        public string SubTasksProgressText => SubTaskHelper.GetProgressText(SubTasksList);
        
        // 子任务是否全部完成
        public bool AllSubTasksCompleted => SubTasksList.Count > 0 && SubTasksList.All(s => s.IsCompleted);
        
        // 子任务数量
        public int SubTasksCount => SubTasksList.Count;
        
        // 是否有子任务
        public bool HasSubTasks => SubTasksList.Count > 0;
    }

    // 子任务模型
    public class SubTask : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private string _title = string.Empty;
        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(); }
        }

        private bool _isCompleted;
        public bool IsCompleted
        {
            get => _isCompleted;
            set { _isCompleted = value; OnPropertyChanged(); }
        }
    }

    // 子任务帮助类
    public static class SubTaskHelper
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public static List<SubTask> ParseSubTasks(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new List<SubTask>();
            try
            {
                return JsonSerializer.Deserialize<List<SubTask>>(json, _jsonOptions) ?? new List<SubTask>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SubTask JSON parse error: {ex.Message}");
                return new List<SubTask>();
            }
        }

        public static string SerializeSubTasks(List<SubTask> subTasks)
        {
            return JsonSerializer.Serialize(subTasks, _jsonOptions);
        }

        public static string SerializeSubTasks(ObservableCollection<SubTask> subTasks)
        {
            return JsonSerializer.Serialize(subTasks.ToList(), _jsonOptions);
        }

        // 获取子任务进度文本，如 "2/5"
        public static string GetProgressText(List<SubTask> subTasks)
        {
            if (subTasks.Count == 0) return "";
            var completed = subTasks.Count(s => s.IsCompleted);
            return $"{completed}/{subTasks.Count}";
        }

        // 获取子任务进度百分比 (0.0 ~ 1.0)
        public static double GetProgress(List<SubTask> subTasks)
        {
            if (subTasks.Count == 0) return 0;
            return (double)subTasks.Count(s => s.IsCompleted) / subTasks.Count;
        }
    }
}
