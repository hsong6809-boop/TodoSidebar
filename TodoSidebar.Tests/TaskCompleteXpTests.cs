using TodoSidebar.Models;
using TodoSidebar.Services;
using Xunit;

namespace TodoSidebar.Tests
{
    /// <summary>B5/B6：task_complete 经验额对称（发/回退共用公式）。</summary>
    public class TaskCompleteXpTests
    {
        [Fact]
        public void Daily_Medium_IsTenPlusPriority()
        {
            var t = new TaskItem { Type = TaskType.Daily, Priority = TaskPriority.Medium };
            // 10 基础 + 3 中优先（无 on-time：每日任务不算 deadline on-time）
            Assert.Equal(13, TaskService.ComputeTaskCompleteXp(t));
        }

        [Fact]
        public void Deadline_High_Future_FifteenPlusOnTimePlusHigh()
        {
            var t = new TaskItem
            {
                Type = TaskType.Deadline,
                Priority = TaskPriority.High,
                Deadline = System.DateTime.Now.AddHours(2)
            };
            // 15 + 5 on-time + 5 high = 25
            Assert.Equal(25, TaskService.ComputeTaskCompleteXp(t));
        }

        [Fact]
        public void Deadline_Low_Past_NoOnTimeBonus()
        {
            var t = new TaskItem
            {
                Type = TaskType.Deadline,
                Priority = TaskPriority.Low,
                Deadline = System.DateTime.Now.AddHours(-2)
            };
            Assert.Equal(15, TaskService.ComputeTaskCompleteXp(t));
        }
    }
}
