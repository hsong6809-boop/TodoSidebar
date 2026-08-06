using System;

namespace TodoSidebar.Models
{
    /// <summary>
    /// 每日挑战记录（对应 DailyChallenge 表）。
    /// </summary>
    public class DailyChallenge
    {
        /// <summary>挑战日期 yyyy-MM-dd</summary>
        public string Date { get; set; } = "";

        /// <summary>挑战类型标识（含目标数，如 complete_daily_tasks_3）</summary>
        public string Type { get; set; } = "";

        public int Progress { get; set; }
        public int Target { get; set; }
        public bool Completed { get; set; }

        /// <summary>展示标题（运行时生成，不持久化）</summary>
        public string Title { get; set; } = "";

        /// <summary>展示图标（运行时生成，不持久化）</summary>
        public string Icon { get; set; } = "🎯";

        /// <summary>完成奖励经验（运行时生成，不持久化）</summary>
        public int Xp { get; set; }
    }
}
