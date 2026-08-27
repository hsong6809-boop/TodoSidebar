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

        // v5.3 回收站
        void RestoreTask(int taskId);
        List<TaskItem> GetDeletedTasks();
        bool PurgeTask(int taskId);
        int PurgeExpiredDeletedTasks();

        // v5.3 热力图
        Dictionary<string, int> GetHeatmapCounts(DateTime start, DateTime end);

        // 设置
        string? GetSetting(string key);
        void SetSetting(string key, string value);

        // 批量操作
        void UpdateTaskOrder(List<(int id, int order)> orders);
        int ImportTasksUnique(List<TaskItem> tasks);

        // 同步支持
        List<TaskItem> GetDirtyTasks();

        /// <summary>R57（审查 M1）：未上云的本地脏数据行数，供切号确认弹窗使用</summary>
        int GetDirtyTaskCount();

        /// <summary>R61（输入统计）：UPSERT 累加当日打字量（仅数量；dateKey=yyyy-MM-dd InvariantCulture）</summary>
        void AddTypingStat(string dateKey, int keyDelta, int wordDelta);

        /// <summary>R61：读取某日打字量；无记录返回 (0, 0)</summary>
        (int KeyStrokes, int WordChars) GetTypingStat(string dateKey);

        void MarkTaskSynced(int localId, string syncId, string? expectedLocalUpdatedAt = null);
        TaskItem? GetTaskBySyncId(string syncId);

        /// <summary>
        /// 通过 SyncId 写入远端任务。R8 修复（审查 M4）：expectedLocalUpdatedAt 为乐观守卫——
        /// 本地行的 LocalUpdatedAt 与预期不符（写入前被并发编辑）时放弃覆盖并返回 false，
        /// 避免远端旧值静默覆盖刚写入的本地修改。
        /// </summary>
        /// <returns>是否实际写入了本地库</returns>
        bool UpsertTaskFromRemote(TaskItem task, string? expectedLocalUpdatedAt = null);
        void PurgeDeletedTasks(int daysOld = 30);

        // 多用户隔离
        void EnsureUserScope(string userId);

        // 成长系统原子操作（M14/M19）
        bool TryRewardXp(string source, int? taskId, string date, bool dedup, Func<UserGrowth, int> mutate);
        int GetDailyTaskCountAsOf(string date);
    }
}
