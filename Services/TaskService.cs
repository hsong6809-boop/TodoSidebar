using System;
using System.Globalization;
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

        // 获取所有每日任务（不管完成状态，完成状态由 DailyTaskCompletion 表追踪）
        public List<TaskItem> GetDailyTasks()
        {
            return _db.GetTasks(TaskType.Daily);
        }

        // 获取今日已完成的每日任务
        public List<TaskItem> GetTodayCompletedDailyTasks()
        {
            return _db.GetTodayCompletedDailyTasks();
        }

        // 获取截止任务（未完成且未过期）。
        // R24 修复（审查 H6）：新增 includeOverdue 参数——通知服务需要包含已逾期任务，
        // 否则「任务已过期」分支的数据源永远不含过期任务、成为死代码
        public List<TaskItem> GetDeadlineTasks(bool includeOverdue = false)
        {
            var today = DateTime.Today;
            IEnumerable<TaskItem> query = _db.GetTasks(TaskType.Deadline, completed: false);
            if (!includeOverdue)
                query = query.Where(t => t.Deadline == null || t.Deadline.Value.Date >= today);
            return query.OrderBy(t => t.Deadline).ToList();
        }

        // 获取当前任务：每日 + 未过期截止任务
        public List<TaskItem> GetCurrentTasks()
        {
            var daily = GetDailyTasks();
            var deadline = GetDeadlineTasks();
            // S11 修复：按 SortOrder 排序（与 ReorderTasks 的排序键一致），
            // 原实现按 Type/Deadline 重排导致拖拽排序写库后被立即打回
            return daily.Concat(deadline)
                .OrderBy(t => t.SortOrder)
                .ThenByDescending(t => t.CreatedAt)
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
                    var today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); // L7 修复
                    _db.MarkDailyTaskCompleted(task.Id, today);
                    _db.MarkTaskDirty(task.Id); // 标记需要同步
                    task.IsTodayCompleted = true;
                    RewardTaskComplete(task);
                }
                else
                {
                    // 截止任务：正常标记完成
                    task.IsCompleted = true;
                    task.CompletedAt = DateTime.Now;
                    _db.UpdateTask(task);
                    RewardTaskComplete(task);

                    // v5.4 重复任务：按规则生成下一期实例（新 Id，独立统计历史）
                    SpawnNextRecurrence(task);
                }
            }
            catch (Exception ex)
            {
                _messageService.ShowError($"完成任务失败: {ex.Message}", "错误");
            }
        }

        /// <summary>
        /// v5.4 重复任务：截止任务完成后按规则派生下一期。
        /// 失败仅记日志——下一期没生成不应让"本次完成"的奖励/状态回滚。
        /// </summary>
        private void SpawnNextRecurrence(TaskItem completedTask)
        {
            try
            {
                if (!completedTask.HasRecurrence) return;

                var baseDate = completedTask.Deadline ?? DateTime.Today;
                var next = RecurrenceRule.NextDeadline(completedTask.Recurrence, baseDate);
                if (next == null) return;

                var nextTask = new TaskItem
                {
                    Title = completedTask.Title,
                    Type = TaskType.Deadline,
                    Priority = completedTask.Priority,
                    Deadline = next,
                    Description = completedTask.Description,
                    Tags = completedTask.Tags,
                    SortOrder = completedTask.SortOrder,
                    EstimatedMinutes = completedTask.EstimatedMinutes,
                    Recurrence = completedTask.Recurrence
                };
                nextTask.Id = _db.InsertTask(nextTask);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SpawnNextRecurrence error: {ex.Message}");
            }
        }

        /// <summary>
        /// 完成任务后发放经验（升级系统打点）：
        /// 每日 +10 / 截止 +15；按时完成 +5；高优先级 +5、中优先级 +3。
        /// </summary>
        private void RewardTaskComplete(TaskItem task)
        {
            try
            {
                int xp = task.Type == TaskType.Deadline ? 15 : 10;

                // 截止任务按时完成奖励
                bool onTime = task.Type == TaskType.Deadline && task.Deadline.HasValue && DateTime.Now <= task.Deadline.Value;
                if (onTime)
                    xp += 5;

                // 优先级加成（Low 无加成）
                xp += task.Priority switch
                {
                    TaskPriority.High => 5,
                    TaskPriority.Medium => 3,
                    _ => 0
                };

                LevelService.Instance.Reward("task_complete", xp, task.Id);

                // 每日挑战进度推进（每日任务 / 按时截止任务）
                if (task.Type == TaskType.Daily)
                    DailyChallengeService.Instance.RegisterProgress("complete_daily_tasks");
                else if (onTime)
                    DailyChallengeService.Instance.RegisterProgress("deadline_on_time");

                // 成就检查（任务类/单日全清/彩蛋徽章）
                AchievementService.Instance.CheckAll();
            }
            catch (Exception ex)
            {
                // 升级/成就/挑战系统异常不应影响任务完成主流程
                System.Diagnostics.Debug.WriteLine($"Reward XP failed: {ex.Message}");
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
                    var today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); // L7 修复
                    _db.UnmarkDailyTaskCompleted(task.Id, today);
                    _db.MarkTaskDirty(task.Id); // 标记需要同步
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

        // ==================== v5.3 回收站 ====================

        public void RestoreTask(int id) => _db.RestoreTask(id);

        public List<TaskItem> GetDeletedTasks() => _db.GetDeletedTasks();

        public bool PurgeTask(int id) => _db.PurgeTask(id);

        public int PurgeExpiredDeletedTasks() => _db.PurgeExpiredDeletedTasks();

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
