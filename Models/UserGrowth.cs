using System;

namespace TodoSidebar.Models
{
    /// <summary>
    /// 用户成长档案（升级系统）。
    /// 对应 UserProfile 表，单行数据（Id = 1）。
    /// </summary>
    public class UserGrowth
    {
        public int Id { get; set; } = 1;

        /// <summary>当前等级（Lv.1 起）</summary>
        public int Level { get; set; } = 1;

        /// <summary>当前等级内的经验值</summary>
        public int Xp { get; set; }

        /// <summary>累计总经验（用于云同步合并取大）</summary>
        public int TotalXp { get; set; }

        /// <summary>当前连击天数（全清每日任务的连续天数）</summary>
        public int ComboDays { get; set; }

        /// <summary>历史最高连击</summary>
        public int BestComboDays { get; set; }

        /// <summary>当前称号</summary>
        public string Title { get; set; } = "初出茅庐";

        /// <summary>最近一次获得经验日期（yyyy-MM-dd，防重复结算）</summary>
        public string? LastXpDate { get; set; }

        /// <summary>
        /// 连击结算游标（yyyy-MM-dd，S9 修复）。
        /// 记录已结算到哪一天，应用错过午夜后可从此处补结算。
        /// </summary>
        public string? LastComboSettledDate { get; set; }
    }

    /// <summary>
    /// 等级信息视图对象（绑定用）
    /// </summary>
    public class LevelInfo
    {
        public int Level { get; set; }
        public string Title { get; set; } = "初出茅庐";
        public int CurrentXp { get; set; }
        public int XpForNext { get; set; }

        /// <summary>当前等级内进度（0~1）</summary>
        public double Progress => XpForNext > 0 ? Math.Min(1.0, (double)CurrentXp / XpForNext) : 0;

        /// <summary>展示文本，如 "340/400 XP"</summary>
        public string ProgressText => $"{CurrentXp}/{XpForNext} XP";

        public string LevelDisplay => $"Lv.{Level} {Title}";
    }

    /// <summary>
    /// 升级事件参数
    /// </summary>
    public class LevelUpEventArgs : EventArgs
    {
        public int NewLevel { get; }
        public string NewTitle { get; }

        public LevelUpEventArgs(int newLevel, string newTitle)
        {
            NewLevel = newLevel;
            NewTitle = newTitle;
        }
    }

    /// <summary>
    /// 连击结算事件参数
    /// </summary>
    public class ComboSettledEventArgs : EventArgs
    {
        /// <summary>结算后的连击天数</summary>
        public int NewComboDays { get; }

        /// <summary>是否断连（清零）</summary>
        public bool Broken { get; }

        public ComboSettledEventArgs(int newComboDays, bool broken)
        {
            NewComboDays = newComboDays;
            Broken = broken;
        }
    }

    /// <summary>
    /// 成就解锁事件参数
    /// </summary>
    public class AchievementUnlockedEventArgs : EventArgs
    {
        public string AchievementId { get; }
        public string Name { get; }
        public string Description { get; }

        public AchievementUnlockedEventArgs(string achievementId, string name, string description)
        {
            AchievementId = achievementId;
            Name = name;
            Description = description;
        }
    }

    /// <summary>
    /// XP 流水记录（同步用）
    /// </summary>
    public class XpLogEntry
    {
        public int Id { get; set; }
        public string Source { get; set; } = "";
        public int Amount { get; set; }
        public int? TaskId { get; set; }
        public string Date { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// 番茄会话记录（同步用）
    /// </summary>
    public class PomodoroSessionEntry
    {
        public int Id { get; set; }
        public int? TaskId { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public int DurationMinutes { get; set; }
        public bool Completed { get; set; }
        public string Date { get; set; } = "";
    }
}
