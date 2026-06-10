using System;
using System.Collections.Generic;
using System.Linq;
using TodoSidebar.Models;

namespace TodoSidebar.Services
{
    public class TaskService
    {
        private readonly DatabaseService _db;
        private readonly IMessageService _messageService;

        public TaskService(DatabaseService db, IMessageService? messageService = null)
        {
            _db = db;
            _messageService = messageService ?? new NullMessageService();
        }

        // 获取今日每日任务
        public List<TaskItem> GetDailyTasks()
        {
            return _db.GetTasks(TaskType.Daily, completed: false);
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
                task.IsCompleted = true;
                task.CompletedAt = DateTime.Now;
                _db.UpdateTask(task);
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
                task.IsCompleted = false;
                task.CompletedAt = null;
                _db.UpdateTask(task);
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
