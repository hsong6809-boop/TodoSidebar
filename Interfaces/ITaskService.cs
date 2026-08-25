using System;
using System.Collections.Generic;
using TodoSidebar.Models;

namespace TodoSidebar.Services
{
    /// <summary>
    /// 任务服务接口。
    /// </summary>
    public interface ITaskService
    {
        List<TaskItem> GetDailyTasks();
        List<TaskItem> GetDeadlineTasks();
        List<TaskItem> GetCurrentTasks();
        List<TaskItem> GetHistoryTasks(DateTime? fromDate = null, DateTime? toDate = null);

        TaskItem AddTask(string title, TaskType type, DateTime? deadline = null, TaskPriority priority = TaskPriority.Medium);
        void CompleteTask(TaskItem task);
        void UncompleteTask(TaskItem task);
        void DeleteTask(int id);
        void UpdateSubTasks(TaskItem task, string subTasksJson);

        // v5.3 回收站
        void RestoreTask(int id);
        List<TaskItem> GetDeletedTasks();
        bool PurgeTask(int id);
        int PurgeExpiredDeletedTasks();
    }
}
