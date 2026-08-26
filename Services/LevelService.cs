using System;
using System.Globalization;
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
        /// 可重复发放的经验来源（S10 修复）：
        /// 这些来源每次完成都独立计奖，不参与"同源同日"防重。
        /// 其余 null-taskId 来源（pomodoro_daily / pomodoro_round / challenge_* / combo）
        /// 均为每日一次性奖励，必须防重。
        /// </summary>
        private static readonly System.Collections.Generic.HashSet<string> RepeatableSources = new()
        {
            "pomodoro" // 未绑定任务的番茄：每个完成会话独立 +5
        };

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

            var date = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); // L7 修复
            var dedup = !RepeatableSources.Contains(source);

            // M14 修复：查重、档案更新、流水写入在单锁单事务内原子完成，
            // 消除并发绕过与"档案已写/流水未写"的崩溃窗口
            var leveledUp = false;
            var newLevel = 0;
            var newTitle = "";
            var applied = _db.TryRewardXp(source, taskId, date, dedup, g =>
            {
                g.Xp += amount;
                g.TotalXp += amount;
                g.LastXpDate = date;
                leveledUp = ApplyLevelUps(g);
                newLevel = g.Level;
                newTitle = g.Title;
                return amount; // 实发经验
            });

            if (!applied) return; // 命中防重，未发放

            // 触发事件（事务提交成功后）
            if (leveledUp)
                LevelUp?.Invoke(this, new LevelUpEventArgs(newLevel, newTitle));
            XpChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 升级循环（可能一次跨多级），返回是否发生了升级。
        /// </summary>
        private bool ApplyLevelUps(UserGrowth growth)
        {
            var leveledUp = false;
            while (growth.Xp >= XpForNextLevel(growth.Level))
            {
                growth.Xp -= XpForNextLevel(growth.Level);
                growth.Level++;
                leveledUp = true;
            }
            if (leveledUp)
                growth.Title = TitleForLevel(growth.Level);
            return leveledUp;
        }

        /// <summary>
        /// 连击每日结算（由午夜定时器或启动补结算触发，幂等）。
        /// S9 修复：从上次结算日循环补结算到昨天——应用错过午夜（最常见场景）
        /// 也能在下次启动时补齐，不再永久漏结算/误断连。
        /// </summary>
        public void SettleCombo() => SettleComboUpTo(DateTime.Today.AddDays(-1));

        /// <summary>
        /// 补结算到指定日期（含）。
        /// 首次使用（无结算游标）只结算昨天，与旧版午夜行为对齐，避免追溯整段历史；
        /// 之后每次从上次结算日的下一天开始逐日结算。
        /// </summary>
        public void SettleComboUpTo(DateTime endDate)
        {
            var growth = GetGrowth();
            DateTime start;
            if (string.IsNullOrEmpty(growth.LastComboSettledDate))
            {
                start = endDate;
            }
            else if (DateTime.TryParseExact(growth.LastComboSettledDate, "yyyy-MM-dd",
                     System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var last))
            {
                start = last.AddDays(1);
                if (start > endDate)
                    return; // 已结算到位
            }
            else
            {
                start = endDate;
            }

            var anyFullClear = false;
            var brokeLast = false;
            var brokeAny = false; // R36 修复（审查 L5）：补结算跨多天时，断连可能发生在中间某天
            for (var date = start; date <= endDate; date = date.AddDays(1))
            {
                var result = SettleComboForDate(date);
                if (result == SettleResult.FullClear) anyFullClear = true;
                brokeLast = result == SettleResult.Broken;
                if (brokeLast) brokeAny = true;
            }

            // 聚合触发一次事件（补结算多天时不刷屏）。
            // R36（审查 L5）：只要发生过断连就触发事件——原实现只看最后一天，
            // 断连在中间天而末日"无任务"时横幅不出现，用户不知道连击已被清零
            if (brokeAny || anyFullClear)
            {
                var final = GetGrowth();
                if (brokeLast)
                    ComboSettled?.Invoke(this, new ComboSettledEventArgs(final.ComboDays, true));
                else
                {
                    ComboSettled?.Invoke(this, new ComboSettledEventArgs(final.ComboDays, brokeAny));
                    XpChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private enum SettleResult { None, FullClear, Broken }

        /// <summary>
        /// 结算指定日期的连击（幂等，由结算游标与 combo 流水双重保证）。
        /// 该日全清 → 连击 +1 并发放连击经验（连击天数 × 2）；
        /// 该日有每日任务但未全清 → 连击清零；无每日任务 → 保持不动。
        /// 每日独立加载档案（M14 复盘：共享可变对象曾导致过期状态覆盖已提交数据）。
        /// </summary>
        private SettleResult SettleComboForDate(DateTime date)
        {
            var dateStr = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); // L7 修复
            var growth = GetGrowth();

            // 幂等兜底：该日已有 combo 流水则只推进游标
            if (_db.HasXpLog("combo", null, dateStr))
            {
                growth.LastComboSettledDate = dateStr;
                _db.SaveUserGrowth(growth);
                return SettleResult.None;
            }

            // M19 修复：用"该日期时点已存在的每日任务数"判定全清，
            // 原实现用当前任务数，今天增删任务会追溯改写历史连击
            var dailyCount = _db.GetDailyTaskCountAsOf(dateStr);
            if (dailyCount <= 0)
            {
                // 没有每日任务，不参与连击，推进游标避免重复扫描
                growth.LastComboSettledDate = dateStr;
                _db.SaveUserGrowth(growth);
                return SettleResult.None;
            }

            var completedOnDate = _db.GetCompletedDailyTaskCountByDate(dateStr);
            var isFullClear = completedOnDate >= dailyCount;

            if (isFullClear)
            {
                // 连击 +1 + 连击经验（连击天数 × 2），M14：原子写入
                var leveledUp = false;
                var newLevel = 0;
                var newTitle = "";
                var applied = _db.TryRewardXp("combo", null, dateStr, dedup: true, g =>
                {
                    g.ComboDays++;
                    if (g.ComboDays > g.BestComboDays)
                        g.BestComboDays = g.ComboDays;

                    var xp = g.ComboDays * 2;
                    g.Xp += xp;
                    g.TotalXp += xp;
                    g.LastXpDate = dateStr;
                    leveledUp = ApplyLevelUps(g);
                    newLevel = g.Level;
                    newTitle = g.Title;
                    return xp; // 实发经验：连击天数 × 2
                });

                if (!applied)
                    return SettleResult.None; // 并发下已被结算

                if (leveledUp)
                    LevelUp?.Invoke(this, new LevelUpEventArgs(newLevel, newTitle));

                // 推进结算游标（重新加载，避免用过期对象覆盖刚提交的数据）
                var fresh = GetGrowth();
                fresh.LastComboSettledDate = dateStr;
                _db.SaveUserGrowth(fresh);
                return SettleResult.FullClear;
            }

            if (growth.ComboDays > 0)
            {
                // 断连清零
                growth.ComboDays = 0;
                growth.LastComboSettledDate = dateStr;
                _db.SaveUserGrowth(growth);
                return SettleResult.Broken;
            }

            growth.LastComboSettledDate = dateStr;
            _db.SaveUserGrowth(growth);
            return SettleResult.None;
        }
    }
}
