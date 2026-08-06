using System;
using TodoSidebar.Models;

namespace TodoSidebar.Services
{
    /// <summary>
    /// 等级/经验服务（升级系统核心）。
    /// 负责 XP 记账（防重）、升级判定、称号映射与升级事件。
    /// </summary>
    public class LevelService
    {
        private static readonly Lazy<LevelService> _lazy = new(() => new LevelService());
        public static LevelService Instance => _lazy.Value;

        private readonly DatabaseService _db;

        /// <summary>升级事件（新等级、新称号）</summary>
        public event EventHandler<LevelUpEventArgs>? LevelUp;

        /// <summary>经验变更事件（任意 XP 增减后触发，UI 刷新经验条用）</summary>
        public event EventHandler? XpChanged;

        /// <summary>连击结算事件（连击 +1 或清零，午夜触发）</summary>
        public event EventHandler<ComboSettledEventArgs>? ComboSettled;

        private LevelService()
        {
            _db = DatabaseService.Instance;
        }

        /// <summary>
        /// 升级所需经验公式：100 + (Lv-1) × 20（温和平滑型，前期升级快、后期靠坚持）。
        /// </summary>
        public static int XpForNextLevel(int level) => 100 + (level - 1) * 20;

        /// <summary>
        /// 由累计总经验推导等级与当前级内经验（跨设备合并时重算本地档案）。
        /// </summary>
        public static (int level, int xp) DeriveFromTotal(int totalXp)
        {
            int level = 1, xp = Math.Max(0, totalXp);
            while (xp >= XpForNextLevel(level))
            {
                xp -= XpForNextLevel(level);
                level++;
            }
            return (level, xp);
        }

        /// <summary>
        /// 等级称号映射（纯视觉荣誉，不锁定任何功能）。
        /// </summary>
        public static string TitleForLevel(int level) => level switch
        {
            >= 99 => "传说冒险者",
            >= 70 => "时间旅人",
            >= 50 => "效率贤者",
            >= 30 => "任务大师",
            >= 20 => "时间掌控者",
            >= 10 => "专注修行者",
            >= 5 => "勤勉学徒",
            _ => "初出茅庐"
        };

        /// <summary>
        /// 等级展示文本；特殊等级（10 的倍数 / Lv.99）追加 ✨ 称号特效。
        /// </summary>
        public static string FormatLevelDisplay(int level, string title)
        {
            bool special = level >= 10 && (level % 10 == 0 || level == 99);
            return special ? $"Lv.{level} {title} ✨" : $"Lv.{level} {title}";
        }

        /// <summary>
        /// 获取当前成长档案（不存在则自动创建默认档案）。
        /// </summary>
        public UserGrowth GetGrowth() => _db.GetUserGrowth();

        /// <summary>
        /// 获取等级信息视图对象（供 UI 绑定）。
        /// </summary>
        public LevelInfo GetLevelInfo(UserGrowth growth)
        {
            return new LevelInfo
            {
                Level = growth.Level,
                Title = growth.Title,
                CurrentXp = growth.Xp,
                XpForNext = XpForNextLevel(growth.Level)
            };
        }

        /// <summary>
        /// 奖励经验（核心入口）。
        /// 防重：同一来源 + 同一任务 + 同一天只结算一次；处理升级并触发事件。
        /// </summary>
        /// <param name="source">来源标识（task_complete / pomodoro / combo / challenge ...）</param>
        /// <param name="amount">经验值（≤0 直接忽略）</param>
        /// <param name="taskId">关联任务（可空，用于防重与追溯）</param>
        public void Reward(string source, int amount, int? taskId = null)
        {
            if (amount <= 0) return;

            var date = DateTime.Today.ToString("yyyy-MM-dd");

            // 防重复结算：同源 + 同任务 + 同日期
            if (taskId.HasValue && _db.HasXpLog(source, taskId, date))
                return;

            var growth = GetGrowth();
            growth.Xp += amount;
            growth.TotalXp += amount;
            growth.LastXpDate = date;

            // 升级循环（可能一次跨多级）
            var leveledUp = false;
            while (growth.Xp >= XpForNextLevel(growth.Level))
            {
                growth.Xp -= XpForNextLevel(growth.Level);
                growth.Level++;
                leveledUp = true;
            }
            if (leveledUp)
            {
                growth.Title = TitleForLevel(growth.Level);
                if (growth.ComboDays > growth.BestComboDays)
                    growth.BestComboDays = growth.ComboDays;
            }

            // 写库
            _db.SaveUserGrowth(growth);
            _db.AddXpLog(source, amount, taskId, date);

            // 触发升级事件（写库成功后）
            if (leveledUp)
                LevelUp?.Invoke(this, new LevelUpEventArgs(growth.Level, growth.Title));

            // 触发经验变更事件（UI 刷新经验条）
            XpChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 连击每日结算（由午夜定时器触发，幂等）。
        /// 昨天完成全部每日任务 → 连击 +1 并发放连击经验（连击天数 × 2）；
        /// 昨天有每日任务但未全清 → 连击清零；无每日任务 → 保持不动。
        /// 当天已有 combo 流水则跳过（防重复结算）。
        /// </summary>
        public void SettleCombo()
        {
            var today = DateTime.Today.ToString("yyyy-MM-dd");
            var yesterday = DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd");

            // 防重：当天已结算过则跳过
            if (_db.HasXpLog("combo", null, today))
                return;

            var dailyCount = _db.GetDailyTaskCount();
            if (dailyCount <= 0)
                return; // 没有每日任务，不参与连击

            var growth = GetGrowth();
            var completedYesterday = _db.GetCompletedDailyTaskCountByDate(yesterday);
            var isFullClear = completedYesterday >= dailyCount;

            if (isFullClear)
            {
                // 连击 +1
                var newCombo = growth.ComboDays + 1;
                growth.ComboDays = newCombo;
                if (newCombo > growth.BestComboDays)
                    growth.BestComboDays = newCombo;

                // 连击经验：连击天数 × 2（升级判定复用内部逻辑）
                var xp = newCombo * 2;
                growth.Xp += xp;
                growth.TotalXp += xp;
                growth.LastXpDate = today;
                var leveledUp = false;
                while (growth.Xp >= XpForNextLevel(growth.Level))
                {
                    growth.Xp -= XpForNextLevel(growth.Level);
                    growth.Level++;
                    leveledUp = true;
                }
                if (leveledUp)
                    growth.Title = TitleForLevel(growth.Level);

                _db.SaveUserGrowth(growth);
                _db.AddXpLog("combo", xp, null, today);

                ComboSettled?.Invoke(this, new ComboSettledEventArgs(newCombo, false));

                if (leveledUp)
                    LevelUp?.Invoke(this, new LevelUpEventArgs(growth.Level, growth.Title));
                else
                    XpChanged?.Invoke(this, EventArgs.Empty);
            }
            else if (growth.ComboDays > 0)
            {
                // 断连清零
                growth.ComboDays = 0;
                _db.SaveUserGrowth(growth);
                ComboSettled?.Invoke(this, new ComboSettledEventArgs(0, true));
            }
        }
    }
}
