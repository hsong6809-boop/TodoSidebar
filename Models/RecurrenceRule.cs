using System;

namespace TodoSidebar.Models
{
    /// <summary>
    /// v5.4 重复任务规则引擎（仅作用于截止任务，每日任务的"每天刷新"机制保持独立）。
    /// 规则编码（存 Tasks.Recurrence / 云端 recurrence 列）：
    ///   null/""   不重复
    ///   daily     每天
    ///   weekdays  工作日（周一至周五）
    ///   weekly:N  每周 N（N: 1=周一 … 7=周日）
    ///   monthly   每月同一天（大月 31 日在小月自动收敛至月末）
    /// 下一期基准 = max(当前截止日期, 今天)——补打卡逾期实例不会生成连锁过期任务。
    /// </summary>
    public static class RecurrenceRule
    {
        public const string Daily = "daily";
        public const string Weekdays = "weekdays";
        public const string WeeklyPrefix = "weekly:";
        public const string Monthly = "monthly";

        /// <summary>UI 下拉选项（值 + 中文标签）。</summary>
        public static readonly (string Value, string Label)[] Options =
        {
            ("",            "不重复"),
            (Daily,         "每天"),
            (Weekdays,      "工作日（一至五）"),
            (WeeklyPrefix + "1", "每周一"),
            (WeeklyPrefix + "2", "每周二"),
            (WeeklyPrefix + "3", "每周三"),
            (WeeklyPrefix + "4", "每周四"),
            (WeeklyPrefix + "5", "每周五"),
            (WeeklyPrefix + "6", "每周六"),
            (WeeklyPrefix + "7", "每周日"),
            (Monthly,       "每月同一天"),
        };

        /// <summary>规则编码是否合法。</summary>
        public static bool IsValid(string? rule)
        {
            if (string.IsNullOrEmpty(rule)) return true;
            var r = rule.Trim().ToLowerInvariant();
            if (r == Daily || r == Weekdays || r == Monthly) return true;
            if (r.StartsWith(WeeklyPrefix, StringComparison.Ordinal)
                && r.Length == WeeklyPrefix.Length + 1
                && char.IsDigit(r[^1]))
            {
                var n = r[^1] - '0';
                return n >= 1 && n <= 7;
            }
            return false;
        }

        /// <summary>规范化（小写去空白）；非法回退 null（不重复）。</summary>
        public static string? Normalize(string? rule)
        {
            if (string.IsNullOrWhiteSpace(rule)) return null;
            var r = rule.Trim().ToLowerInvariant();
            return IsValid(r) ? r : null;
        }

        /// <summary>中文标签；空规则返回"不重复"。</summary>
        public static string LabelOf(string? rule)
        {
            var n = Normalize(rule);
            if (n == null) return Options[0].Label;
            foreach (var (value, label) in Options)
                if (value == n) return label;
            return Options[0].Label;
        }

        /// <summary>
        /// B4：循环派生幂等判定（纯函数）。
        /// 存在任意存活（未删除）同系列实例落在 <paramref name="nextDeadline"/> 当天 → 不重复派生。
        /// </summary>
        /// <param name="seriesTitle">系列标题（与父实例 Title 相同）</param>
        /// <param name="rule">重复规则</param>
        /// <param name="nextDeadline">拟派生的下一期截止日</param>
        /// <param name="existing">候选实例（同 Title+Recurrence 的行；传入前可先过滤）</param>
        public static bool HasLiveSpawnFor(
            string seriesTitle,
            string? rule,
            DateTime nextDeadline,
            System.Collections.Generic.IEnumerable<(string Title, string? Recurrence, DateTime? Deadline, bool IsDeleted)> existing)
        {
            if (existing == null) return false;
            var nextDate = nextDeadline.Date;
            foreach (var t in existing)
            {
                if (t.IsDeleted) continue;
                if (!string.Equals(t.Title, seriesTitle, StringComparison.Ordinal)) continue;
                if (Normalize(t.Recurrence) != Normalize(rule)) continue;
                if (t.Deadline.HasValue && t.Deadline.Value.Date == nextDate)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// B4：取消完成时应收回的派生实例 Id（仍存活且未完成的下一期）。
        /// 用完成实例的 Deadline 重算 next 作为锚点；存在多个匹配时全部收回（幽灵清理）。
        /// </summary>
        public static System.Collections.Generic.List<int> FindSpawnsToRetract(
            string seriesTitle,
            string? rule,
            DateTime completedDeadline,
            System.Collections.Generic.IEnumerable<(int Id, string Title, string? Recurrence, DateTime? Deadline, bool IsDeleted, bool IsCompleted)> candidates,
            DateTime? today = null)
        {
            var result = new System.Collections.Generic.List<int>();
            var next = NextDeadline(rule, completedDeadline, today);
            if (next == null) return result;
            var nextDate = next.Value.Date;
            if (candidates == null) return result;
            foreach (var t in candidates)
            {
                if (t.IsDeleted || t.IsCompleted) continue;
                if (!string.Equals(t.Title, seriesTitle, StringComparison.Ordinal)) continue;
                if (Normalize(t.Recurrence) != Normalize(rule)) continue;
                if (t.Deadline.HasValue && t.Deadline.Value.Date == nextDate)
                    result.Add(t.Id);
            }
            return result;
        }

        /// <summary>
        /// 计算下一期截止日期。
        /// baseDate 为刚完成实例的截止日期（内部自动取 max(baseDate, today) 防连锁过期）。
        /// 无下一期（规则为空/非法）返回 null。
        /// </summary>
        public static DateTime? NextDeadline(string? rule, DateTime baseDeadline, DateTime? today = null)
        {
            var r = Normalize(rule);
            if (r == null) return null;

            var todayDate = (today ?? DateTime.Today).Date;
            var current = baseDeadline.Date < todayDate ? todayDate : baseDeadline.Date;

            switch (r)
            {
                case Daily:
                    return current.AddDays(1);

                case Weekdays:
                    var wd = current.AddDays(1);
                    while (wd.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                        wd = wd.AddDays(1);
                    return wd;

                case Monthly:
                    // 下一个月的同一天；超出月末天数时收敛到月末（1/31 → 2/28）。
                    // 注意：收敛是永久性的——2/28 完成后下一期按 28 日推（3/28、4/28…），
                    // 不会"补回"31 日。有意简化：不跨月记住原始锚点日，避免状态外置。
                    var month = current.Month == 12 ? new DateTime(current.Year + 1, 1, 1)
                                                    : new DateTime(current.Year, current.Month + 1, 1);
                    var day = Math.Min(current.Day, DateTime.DaysInMonth(month.Year, month.Month));
                    return new DateTime(month.Year, month.Month, day);

                default:
                    if (r.StartsWith(WeeklyPrefix, StringComparison.Ordinal)
                        && int.TryParse(r.AsSpan(WeeklyPrefix.Length), out var target)
                        && target >= 1 && target <= 7)
                    {
                        // DotNet: Sunday=0 … Saturday=6；我们的编码：1=周一 … 7=周日
                        var targetDow = target == 7 ? DayOfWeek.Sunday : (DayOfWeek)target;
                        var next = current.AddDays(1);
                        while (next.DayOfWeek != targetDow)
                            next = next.AddDays(1);
                        return next;
                    }
                    return null;
            }
        }
    }
}
