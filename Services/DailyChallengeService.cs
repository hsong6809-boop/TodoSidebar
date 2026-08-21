using System;
using System.Collections.Generic;
using System.Linq;
using TodoSidebar.Models;

namespace TodoSidebar.Services
{
    /// <summary>
    /// 每日挑战服务（升级系统 P4）。
    /// 每天用种子算法生成 3 个挑战（当天固定、次日刷新），
    /// 由任务/番茄完成事件推进进度，达标发 XP（防重：同类型同天只发一次）。
    /// </summary>
    public class DailyChallengeService
    {
        private static readonly Lazy<DailyChallengeService> _lazy = new(() => new DailyChallengeService());
        public static DailyChallengeService Instance => _lazy.Value;

        private readonly DatabaseService _db;

        /// <summary>挑战更新事件（进度变化/新一天生成，UI 刷新用）</summary>
        public event EventHandler? ChallengesUpdated;

        private DailyChallengeService()
        {
            _db = DatabaseService.Instance;
        }

        /// <summary>
        /// 挑战模板池：类型前缀 + 变体（目标数 + 经验）。
        /// </summary>
        private static readonly (string type, string title, string icon, int target, int xp)[] Pool =
        {
            ("complete_daily_tasks", "完成每日任务", "🗓️", 3, 20),
            ("complete_daily_tasks", "完成每日任务", "🗓️", 5, 25),
            ("complete_pomodoros", "完成番茄专注", "🍅", 2, 20),
            ("complete_pomodoros", "完成番茄专注", "🍅", 4, 30),
            ("deadline_on_time", "按时完成截止任务", "⏰", 1, 25)
        };

        /// <summary>
        /// 获取今日挑战（不存在则按当日种子生成并入库）。
        /// DB 读回后补全运行时元数据（标题/图标/奖励经验）。
        /// </summary>
        public List<DailyChallenge> GetTodayChallenges()
        {
            var today = DateTime.Today.ToString("yyyy-MM-dd");
            var challenges = _db.GetDailyChallenges(today);
            if (challenges.Count == 0)
            {
                challenges = GenerateForDate(today);
                _db.SaveDailyChallenges(today, challenges);
                return challenges;
            }

            foreach (var c in challenges)
            {
                var (title, icon, xp) = LookupMeta(c.Type);
                c.Title = title;
                c.Icon = icon;
                c.Xp = xp;
            }
            return challenges;
        }

        /// <summary>
        /// 按挑战类型补全展示元数据（标题/图标/奖励经验）。
        /// </summary>
        private static (string title, string icon, int xp) LookupMeta(string type)
        {
            foreach (var (prefix, title, icon, target, xp) in Pool)
            {
                if (type == $"{prefix}_{target}")
                    return (title, icon, xp);
            }
            return ("每日挑战", "🎯", 20);
        }

        /// <summary>
        /// 推进指定类型挑战进度（任务/番茄完成事件调用）。
        /// 类型前缀匹配（如 "complete_daily_tasks" 匹配 _3 与 _5 变体）。
        /// </summary>
        public void RegisterProgress(string typePrefix, int amount = 1)
        {
            var today = DateTime.Today.ToString("yyyy-MM-dd");
            var challenges = _db.GetDailyChallenges(today);
            var changed = false;

            foreach (var c in challenges)
            {
                if (c.Completed || !c.Type.StartsWith(typePrefix, StringComparison.Ordinal))
                    continue;

                c.Progress = Math.Min(c.Target, c.Progress + amount);
                changed = true;

                if (c.Progress >= c.Target)
                {
                    c.Completed = true;
                    // 补全奖励经验（DB 不持久化运行时字段）
                    var xp = LookupMeta(c.Type).xp;
                    // 发 XP（source 含类型，防重：同类型同天只发一次）
                    try
                    {
                        LevelService.Instance.Reward($"challenge_{c.Type}", xp, null);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Challenge reward failed: {ex.Message}");
                    }
                }
            }

            if (changed)
            {
                _db.SaveDailyChallenges(today, challenges);
                ChallengesUpdated?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// 用日期种子生成当日 3 个挑战（同一天结果固定）。
        /// M18 修复：string.GetHashCode 在 .NET 8 中是进程级随机化的，
        /// 重启/多设备会生成不同挑战；改用稳定的 FNV-1a 哈希保证跨进程一致。
        /// </summary>
        private static List<DailyChallenge> GenerateForDate(string date)
        {
            unchecked
            {
                var seed = (int)2166136261; // FNV-1a offset basis
                foreach (var c in date)
                    seed = (seed ^ c) * 16777619;
                var rng = new Random(seed);
                var selected = Pool.OrderBy(_ => rng.Next()).Take(3).ToList();

                return selected.Select((t, i) => new DailyChallenge
                {
                    Date = date,
                    Type = $"{t.type}_{t.target}",
                    Title = t.title,
                    Icon = t.icon,
                    Target = t.target,
                    Xp = t.xp,
                    Progress = 0,
                    Completed = false
                }).ToList();
            }
        }
    }
}
