using System;
using System.Collections.Generic;
using TodoSidebar.Models;

namespace TodoSidebar.Services
{
    /// <summary>
    /// 成就徽章系统（升级系统 P3）。
    /// 定义徽章清单、统计指标收集、条件判定与解锁事件。
    /// </summary>
    public class AchievementService
    {
        private static readonly Lazy<AchievementService> _lazy = new(() => new AchievementService());
        public static AchievementService Instance => _lazy.Value;

        private readonly DatabaseService _db;

        /// <summary>徽章解锁事件（UI 弹窗/横幅用）</summary>
        public event EventHandler<AchievementUnlockedEventArgs>? AchievementUnlocked;

        private AchievementService()
        {
            _db = DatabaseService.Instance;
        }

        /// <summary>
        /// 检查全部未解锁徽章（幂等：已解锁跳过）。
        /// 由任务完成 / 番茄完成 / 连击结算 / 启动加载时调用。
        /// </summary>
        public void CheckAll()
        {
            try
            {
                var unlocked = _db.GetUnlockedAchievements();
                var stats = GatherStats();

                foreach (var def in Definitions)
                {
                    if (unlocked.Contains(def.Id))
                        continue;
                    if (!def.IsSatisfied(stats))
                        continue;

                    _db.UnlockAchievement(def.Id);
                    AchievementUnlocked?.Invoke(this,
                        new AchievementUnlockedEventArgs(def.Id, def.Name, def.Description));
                }
            }
            catch (Exception ex)
            {
                // 成就系统异常不应影响主流程
                System.Diagnostics.Debug.WriteLine($"Achievement check failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取全部徽章定义（图鉴展示用）。
        /// </summary>
        public IReadOnlyList<AchievementDef> GetDefinitions() => Definitions;

        private AchievementStats GatherStats()
        {
            var growth = LevelService.Instance.GetGrowth();
            var today = DateTime.Today.ToString("yyyy-MM-dd");

            return new AchievementStats
            {
                TaskCompleteCount = _db.GetXpLogCount("task_complete"),
                PomodoroCompletedTotal = _db.GetCompletedPomodoroTotal(),
                TodayPomodoro = _db.GetPomodoroCountByDate(today).completed,
                DailyTaskCount = _db.GetDailyTaskCount(),
                TodayCompletedDaily = _db.GetCompletedDailyTaskCountByDate(today),
                BestComboDays = growth.BestComboDays,
                ComboDays = growth.ComboDays,
                Level = growth.Level,
                HasEarlyBird = _db.HasXpLogMatchingTime("task_complete", t => t.Hour < 6),
                HasNightOwl = _db.HasXpLogMatchingTime("task_complete", t => t.Hour >= 23)
            };
        }

        // ==================== 徽章清单（首批 20 枚） ====================

        private static readonly List<AchievementDef> Definitions = new()
        {
            // 任务类
            new("task_1", "新手上路", "完成第 1 个任务", s => s.TaskCompleteCount >= 1, "🎯"),
            new("task_10", "小有所成", "累计完成 10 个任务", s => s.TaskCompleteCount >= 10, "🎯"),
            new("task_50", "勤能补拙", "累计完成 50 个任务", s => s.TaskCompleteCount >= 50, "🎯"),
            new("task_100", "百炼成钢", "累计完成 100 个任务", s => s.TaskCompleteCount >= 100, "🎯"),
            new("task_500", "任务传奇", "累计完成 500 个任务", s => s.TaskCompleteCount >= 500, "🎯"),

            // 专注类
            new("pomo_1", "初尝专注", "完成第 1 个番茄", s => s.PomodoroCompletedTotal >= 1, "🍅"),
            new("pomo_10", "番茄入门", "累计完成 10 个番茄", s => s.PomodoroCompletedTotal >= 10, "🍅"),
            new("pomo_25", "番茄达人", "累计完成 25 个番茄", s => s.PomodoroCompletedTotal >= 25, "🍅"),
            new("pomo_50", "番茄大师", "累计完成 50 个番茄", s => s.PomodoroCompletedTotal >= 50, "🍅"),
            new("pomo_100", "番茄传奇", "累计完成 100 个番茄", s => s.PomodoroCompletedTotal >= 100, "🍅"),
            new("pomo_8_day", "全神贯注", "单日完成 8 个番茄", s => s.TodayPomodoro >= 8, "🍅"),

            // 连击类
            new("combo_3", "小试牛刀", "达成 3 天连击", s => s.BestComboDays >= 3, "🔥"),
            new("combo_7", "七日之约", "达成 7 天连击", s => s.BestComboDays >= 7, "🔥"),
            new("combo_30", "月度冠军", "达成 30 天连击", s => s.BestComboDays >= 30, "🔥"),
            new("combo_100", "百日筑基", "达成 100 天连击", s => s.BestComboDays >= 100, "🔥"),

            // 单日与等级
            new("daily_all", "今日完胜", "清空当天全部每日任务", s => s.TodayFullClear, "🌞"),
            new("level_5", "初露锋芒", "等级达到 Lv.5", s => s.Level >= 5, "⭐"),
            new("level_10", "小有名气", "等级达到 Lv.10", s => s.Level >= 10, "⭐"),

            // 彩蛋
            new("early_bird", "早起鸟", "在早上 6 点前完成任务", s => s.HasEarlyBird, "🌅"),
            new("night_owl", "夜猫子", "在深夜 23 点后完成任务", s => s.HasNightOwl, "🌙")
        };
    }

    /// <summary>
    /// 成就判定所需的统计指标快照（内部使用）。
    /// </summary>
    internal sealed class AchievementStats
    {
        public int TaskCompleteCount;
        public int PomodoroCompletedTotal;
        public int TodayPomodoro;
        public int DailyTaskCount;
        public int TodayCompletedDaily;
        public int BestComboDays;
        public int ComboDays;
        public int Level;
        public bool HasEarlyBird;
        public bool HasNightOwl;

        public bool TodayFullClear => DailyTaskCount > 0 && TodayCompletedDaily >= DailyTaskCount;
    }

    /// <summary>
    /// 成就徽章定义
    /// </summary>
    public class AchievementDef
    {
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public string Icon { get; }

        private readonly Func<AchievementStats, bool> _satisfied;

        internal AchievementDef(string id, string name, string description, Func<AchievementStats, bool> satisfied, string icon = "🏅")
        {
            Id = id;
            Name = name;
            Description = description;
            _satisfied = satisfied;
            Icon = icon;
        }

        /// <summary>条件判定（内部使用）</summary>
        internal bool IsSatisfied(AchievementStats stats) => _satisfied(stats);
    }
}
