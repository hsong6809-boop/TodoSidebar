using System;
using System.Collections.Generic;
using System.Linq;
using TodoSidebar.Models;

namespace TodoSidebar.Services
{
    public class TaskService : ITaskService
    {
        private readonly DatabaseService _db;
        private readonly IMessageService _messageService;

        public TaskService(DatabaseService db, IMessageService? messageService = null)
        {
            _db = db;
            _messageService = messageService ?? new NullMessageService();
        }

        // 获取所有每日任务（永远返回，不管完成状态）
        public List<TaskItem> GetDailyTasks()
        {
            return _db.GetTasks(TaskType.Daily, completed: false);
        }

        // 获取今日已完成的每日任务
        public List<TaskItem> GetTodayCompletedDailyTasks()
        {
            return _db.GetTodayCompletedDailyTasks();
        }

        // 获取截止任务（未完成且未过期）
        public List<TaskItem> GetDeadlineTasks()
        {
            var today = DateTime.Today;
            return _db.GetTasks(TaskType.Deadline, completed: false)
                .Where(t => t.Deadline == null || t.Deadline.Value.Date >= today)
                .OrderBy(t => t.Deadline)
                .ToList();
        }

        // 获取当前任务：每日 + 未过期截止任务
        public List<TaskItem> GetCurrentTasks()
        {
            var daily = GetDailyTasks();
            var deadline = GetDeadlineTasks();
            return daily.Concat(deadline)
                .OrderBy(t => t.Type)
                .ThenBy(t => t.Deadline)
                .ToList();
        }


        // 添加任务
        public TaskItem AddTask(string title, TaskType type, DateTime? deadline = null, TaskPriority priority = TaskPriority.Medium)
        {
            var task = new TaskItem
            {
                Title = title,
                Type = type,
                Priority = priority,
                Deadline = deadline
            };
            task.Id = _db.InsertTask(task);
            return task;
        }

        // 完成任务
        public void CompleteTask(TaskItem task)
        {
            try
            {
                if (task.Type == TaskType.Daily)
                {
                    // 每日任务：记录今天的完成状态，不修改任务本身的 IsCompleted
                    var today = DateTime.Today.ToString("yyyy-MM-dd");
                    _db.MarkDailyTaskCompleted(task.Id, today);
                    task.IsTodayCompleted = true;
                }
                else
                {
                    // 截止任务：正常标记完成
                    task.IsCompleted = true;
                    task.CompletedAt = DateTime.Now;
                    _db.UpdateTask(task);
                }
            }
            catch (Exception ex)
            {
                _messageService.ShowError($"完成任务失败: {ex.Message}", "错误");
            }
        }

        // 取消完成任务
        public void UncompleteTask(TaskItem task)
        {
            try
            {
                if (task.Type == TaskType.Daily)
                {
                    // 每日任务：删除今天的完成记录
                    var today = DateTime.Today.ToString("yyyy-MM-dd");
                    _db.UnmarkDailyTaskCompleted(task.Id, today);
                    task.IsTodayCompleted = false;
                }
                else
                {
                    // 截止任务：正常恢复
                    task.IsCompleted = false;
                    task.CompletedAt = null;
                    _db.UpdateTask(task);
                }
            }
            catch (Exception ex)
            {
                _messageService.ShowError($"恢复任务失败: {ex.Message}", "错误");
            }
        }

        // 删除任务
        public void DeleteTask(int id)
        {
            _db.DeleteTask(id);
        }

        // 获取历史完成任务
        public List<TaskItem> GetHistoryTasks(DateTime? fromDate = null, DateTime? toDate = null)
        {
            return _db.GetCompletedTasks(fromDate, toDate);
        }

        // 更新任务的子任务
        public void UpdateSubTasks(TaskItem task, string subTasksJson)
        {
            task.SubTasksJson = subTasksJson;
            _db.UpdateTask(task);
        }

            }
}
