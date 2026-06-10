using System;
using System.Collections.Generic;
using TodoSidebar.Models;

namespace TodoSidebar.Services
{
    /// <summary>
    /// 数据库服务接口。
    /// </summary>
    public interface IDatabaseService
    {
        void Initialize();

        // 任务 CRUD
        int InsertTask(TaskItem task);
        void UpdateTask(TaskItem task);
        void DeleteTask(int taskId);
        TaskItem? GetTaskById(int taskId);
        List<TaskItem> GetTasks(TaskType? type = null, bool? completed = null);
        List<TaskItem> GetCompletedTasks(DateTime? fromDate = null, DateTime? toDate = null);
        List<TaskItem> GetTasks();  // 获取所有任务（用于导出）
        List<TaskItem> SearchTasks(string keyword, TaskType? type = null, TaskPriority? priority = null);

        // 设置
        string? GetSetting(string key);
        void SetSetting(string key, string value);

        // 批量操作
        void UpdateTaskOrder(List<(int id, int order)> orders);

        // 同步支持
        List<TaskItem> GetDirtyTasks();
        void MarkTaskSynced(int localId, string syncId);
        TaskItem? GetTaskBySyncId(string syncId);
        void UpsertTaskFromRemote(TaskItem task);
        void PurgeDeletedTasks(int daysOld = 30);
    }
}
