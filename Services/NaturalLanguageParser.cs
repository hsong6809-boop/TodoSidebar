using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using TodoSidebar.Models;

namespace TodoSidebar.Services
{
    /// <summary>自然语言解析结果。</summary>
    public sealed class ParsedTask
    {
        public string Title { get; set; } = string.Empty;
        public DateTime? DueDate { get; set; }
        public TaskPriority? Priority { get; set; }
        public List<string> Tags { get; } = new();
        public bool HasDue => DueDate.HasValue;
    }

    /// <summary>
    /// V5.1：中文自然语言快速录入解析器（纯静态、无副作用）。
    /// 支持语法（均可混用）：
    ///   #标签            —— 标签（可多个）
    ///   紧急 / 高优 / 重要 → 高优先；低优 / 不重要 → 低优先
    ///   今天 / 明天 / 后天 / 大后天 [上午|下午|晚上 HH[点|:半|:mm]]
    ///   下?周[一二三四五六日天] [时间]
    ///   M月d日[号] [时间]
    ///   HH:mm / X点半 / 下午X点  （裸时间：已过则顺延到明天）
    ///   N小时后 / 半小时后 / N分钟后 / N天后
    /// </summary>
    public static class NaturalLanguageParser
    {
        private static readonly Regex TagRx =
            new(@"#(?<t>[^\s#，,。.!！？?]+)", RegexOptions.Compiled);

        private static readonly Regex WeekdayRx =
            new(@"(?<next>下)?(?:周|星期)(?<w>[一二三四五六日天])", RegexOptions.Compiled);

        private static readonly Regex RelDayRx =
            new(@"(?<w>今天|明天|后天|大后天)", RegexOptions.Compiled);

        private static readonly Regex MdRx =
            new(@"(?<m>\d{1,2})月(?<d>\d{1,2})[日号]", RegexOptions.Compiled);

        private static readonly Regex TimeRx =
            new(@"(?<ap>上午|中午|下午|晚上)?\s*(?<hh>\d{1,2})[点时:：]\s*(?<mm>半|\d{1,2})?", RegexOptions.Compiled);

        private static readonly Regex RelHoursRx =
            new(@"(?<h>\d+(?:\.\d+)?)\s*(?:个)?小时后"
                + @"|(?<halfnum>\d+(?:\.\d+)?)\s*个?半\s*小时后"
                + @"|(?<halfcn>一)个?半\s*小时后"
                + @"|半小时后",
                RegexOptions.Compiled);

        private static readonly Regex RelMinutesRx =
            new(@"(?<m>\d+)\s*分钟后", RegexOptions.Compiled);

        private static readonly Regex SpaceCollapse = new(@"\s{2,}", RegexOptions.Compiled);

        public static ParsedTask Parse(string? raw)
        {
            var result = new ParsedTask();
            if (string.IsNullOrWhiteSpace(raw)) return result;

            var text = raw.Trim();

            // ---- 标签 ----
            foreach (Match m in TagRx.Matches(text).Cast<Match>())
            {
                var tag = m.Groups["t"].Value.Trim();
                if (tag.Length > 0 && !result.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                    result.Tags.Add(tag);
            }
            text = TagRx.Replace(text, " ");

            // ---- 优先级 ----
            if (Regex.IsMatch(text, "紧急|高优|重要"))
            {
                result.Priority = TaskPriority.High;
                text = Regex.Replace(text, "紧急|高优|重要", " ");
            }
            if (Regex.IsMatch(text, "低优|不重要"))
            {
                result.Priority ??= TaskPriority.Low;
                text = Regex.Replace(text, "低优|不重要", " ");
            }

            // ---- 相对小时 ----
            var mh = RelHoursRx.Match(text);
            if (mh.Success && !result.DueDate.HasValue)
            {
                double hours;
                // R30 修复（审查 NLP-L1）：「一个半小时后」此前在“半小时后”分支命中、被算成 0.5 小时；
                // 现按 N + 0.5 计算（「1个半小时后」= 1.5，「一个半小时后」= 1.5，「2个半小时后」= 2.5）
                if (mh.Groups["halfnum"].Success)
                {
                    var n = double.TryParse(mh.Groups["halfnum"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var nn) ? nn : 1;
                    hours = n + 0.5;
                }
                else if (mh.Groups["halfcn"].Success)
                {
                    hours = 1.5; // 中文数字「一个半小时」
                }
                else if (text.Contains("半小时") && mh.Groups["h"].Value.Length == 0) hours = 0.5;
                else if (!double.TryParse(mh.Groups["h"].Success ? mh.Groups["h"].Value : "1", NumberStyles.Float, CultureInfo.InvariantCulture, out var h))
                    hours = 1;
                else hours = h;
                result.DueDate = DateTime.Now.AddHours(hours);
                text = RelHoursRx.Replace(text, " ", 1);
            }

            var mm = RelMinutesRx.Match(text);
            if (mm.Success && !result.DueDate.HasValue)
            {
                if (int.TryParse(mm.Groups["m"].Value, out var mins))
                    result.DueDate = DateTime.Now.AddMinutes(mins);
                text = RelMinutesRx.Replace(text, " ", 1);
            }

            // ---- 星期几（本周最近一次或下一自然周）----
            var mw = WeekdayRx.Match(text);
            if (mw.Success && !result.DueDate.HasValue)
            {
                var target = WeekdayToNumber(mw.Groups["w"].Value[0]);
                if (mw.Groups["next"].Success)
                {
                    // “下周三”：按自然周（周一为一周起点）落到下一周的该星期。
                    // 例如今天是周五，“下周三” = 下周一 + 2 天 = 5 天后。
                    var todayDow = (int)DateTime.Today.DayOfWeek;   // Sun=0 … Sat=6
                    var daysToNextMonday = (8 - todayDow) % 7;      // 0 表示今天就是周一
                    if (daysToNextMonday == 0) daysToNextMonday = 7;
                    var nextMonday = DateTime.Today.AddDays(daysToNextMonday);
                    var offsetAfterMonday = target == DayOfWeek.Sunday ? 6 : (int)target - 1;
                    result.DueDate = nextMonday.AddDays(offsetAfterMonday);
                }
                else
                {
                    var delta = ((int)target - (int)DateTime.Today.DayOfWeek + 7) % 7; // 含当天：周三说“周三”即今天
                    result.DueDate = DateTime.Today.AddDays(delta);
                }
                text = WeekdayRx.Replace(text, " ", 1);

                // R31 修复（审查 NLP-L12）：星期词命中后相对日分支会因门控跳过，
                // 「明天周三交周报」的"明天"会残留在标题里——补一次清理
                text = RelDayRx.Replace(text, " ");
            }

            // ---- 今天 / 明天 / 后天 / 大后天 ----
            var mr = RelDayRx.Match(text);
            if (mr.Success && !result.DueDate.HasValue)
            {
                var offset = mr.Groups["w"].Value switch
                {
                    "明天" => 1,
                    "后天" => 2,
                    "大后天" => 3,
                    _ => 0
                };
                result.DueDate = DateTime.Today.AddDays(offset);
                text = RelDayRx.Replace(text, " ", 1);
            }

            // ---- M月d日 ----
            var mmd = MdRx.Match(text);
            if (mmd.Success && !result.DueDate.HasValue)
            {
                if (int.TryParse(mmd.Groups["m"].Value, out var mon) &&
                    int.TryParse(mmd.Groups["d"].Value, out var day))
                {
                    var year = DateTime.Today.Year;
                    // R26 修复（审查 NLP-L2）：同月但已过的日期同样折算到明年，
                    // 否则「12月1日」在 12 月 15 日输入会解析出过去日期、必然被表单拦截
                    if (mon < DateTime.Today.Month ||
                        (mon == DateTime.Today.Month && day < DateTime.Today.Day))
                        year++;
                    // R25 修复（审查 H7）：SafeDate 失败（4月31日/2月30日等）返回 null，
                    // DueDate 保持为空；绝不再静默回落到「今天」并落库
                    result.DueDate = SafeDate(year, mon, day);
                }
                text = MdRx.Replace(text, " ", 1);
            }

            // ---- 裸时间（无日期时才作为定位）----
            var mt = TimeRx.Match(text);
            TimeSpan? timeOfDay = null;
            if (mt.Success)
            {
                timeOfDay = ParseTime(mt);
                // R28 修复（审查 M4）：仅当解析成功才从标题剥离时间片段——
                // 原实现无论合法与否一律删除，「25点开会」的"25点"凭空消失且无任何提示
                if (timeOfDay.HasValue)
                    text = text.Remove(mt.Index, mt.Length).Insert(mt.Index, " ");
            }

            if (timeOfDay.HasValue)
            {
                if (!result.DueDate.HasValue)
                {
                    var baseDate = DateTime.Today;
                    var candidate = baseDate + timeOfDay.Value;
                    if (candidate <= DateTime.Now) candidate = candidate.AddDays(1);
                    result.DueDate = candidate;
                }
                else
                {
                    result.DueDate = result.DueDate!.Value.Date + timeOfDay.Value;
                }
            }

            // ---- 清理标题 ----
            result.Title = SpaceCollapse.Replace(text, " ").Trim(' ', '　', '-', '—', '，', ',', '。', '.');
            return result;
        }

        private static DayOfWeek WeekdayToNumber(char c) => c switch
        {
            '一' => DayOfWeek.Monday,
            '二' => DayOfWeek.Tuesday,
            '三' => DayOfWeek.Wednesday,
            '四' => DayOfWeek.Thursday,
            '五' => DayOfWeek.Friday,
            '六' => DayOfWeek.Saturday,
            _ => DayOfWeek.Sunday
        };

        private static DateTime? SafeDate(int y, int m, int d)
        {
            // R25 修复（审查 H7）：不存在的日期（4月31日、平年2月29日、13月1日等）
            // 返回 null 让 DueDate 保持为空；原实现静默回落到「今天」并落库，
            // 任务被悄悄安排成今天到期还触发今日通知，用户极难察觉
            try { return new DateTime(y, m, d); }
            catch { return null; }
        }

        /// <summary>把“上午3点/晚上10点半/14:30”等片段解析为时刻。</summary>
        private static TimeSpan? ParseTime(Match mt)
        {
            if (!int.TryParse(mt.Groups["hh"].Value, out var hh)) return null;
            if (hh > 24) return null;

            var mmRaw = mt.Groups["mm"].Value;
            // R29 修复（审查 v5.x-L8）：裸冒号形式（"15:xx"）必须带分钟才认，
            // 避免「版本15:新特性」这类文本被误当时间剥离并把 due 设到 15:00
            if ((mt.Value.Contains(':') || mt.Value.Contains('：')) && mmRaw.Length == 0)
                return null;

            int mm = 0;
            if (mmRaw == "半") mm = 30;
            else if (mmRaw.Length > 0 && !int.TryParse(mmRaw, out mm)) return null;

            var ap = mt.Groups["ap"].Value;
            // R27 修复（审查 M1）：12 小时制边界——
            // 「晚上12点」= 午夜 0 点（原实现错成中午 12:00）；
            // 「中午1点」= 13:00（原实现完全忽略"中午"、错成凌晨 01:00）
            switch (ap)
            {
                case "中午":
                    if (hh < 12) hh += 12;          // 中午12点保持 12:00
                    break;
                case "下午":
                    if (hh < 12) hh += 12;          // 下午12点保持 12:00（正午）
                    break;
                case "晚上":
                    if (hh < 12) hh += 12;
                    else if (hh == 12) hh = 0;      // 晚上12点 = 午夜
                    break;
                case "上午":
                    if (hh == 12) hh = 0;           // 上午12点按 0 点处理
                    break;
            }

            if (hh > 24 || mm > 59) return null;
            return new TimeSpan(hh == 24 ? 0 : hh, mm, 0);
        }
    }
}
