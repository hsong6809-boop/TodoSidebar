using System;

namespace TodoSidebar.Services
{
    /// <summary>
    /// B3：今日进度共享口径（侧边栏 MainViewModel 与统计页 StatisticsViewModel 共用）。
    ///
    /// 用户定义（R63/R64 + B3 收口）：
    ///   分母 M = 当日计划量 = 全部每日任务 + 今日到期截止任务（含已完成的到期任务），
    ///            即"今天本来该做的事"，不随额外完成而增长；
    ///   分子 N = 今日全部完成数（每日今日完成 + 今日完成的截止任务，含补做逾期/提前完成）；
    ///   rate   = clamp(N / M, 0, 1)；
    ///   超额   = N > M（✨ 超额完成）。
    /// B3 修复：原统计页分母漏掉「今日已完成的截止任务」，同一天两套完成率。
    /// </summary>
    public static class TodayProgressCalculator
    {
        /// <summary>进度计算结果。</summary>
        public readonly record struct Result(
            int Done,
            int Planned,
            double Rate,
            bool IsOverachieving,
            string DoneText);

        /// <summary>
        /// 计算今日进度。
        /// </summary>
        /// <param name="completedToday">N：今日完成总数（每日今日完成 + 今日完成的任意截止任务）</param>
        /// <param name="dailyTaskCount">当日每日任务总数（含今日已完成的每日任务）</param>
        /// <param name="dueTodayCount">今日到期截止任务总数（含已完成的到期任务，不含逾期/未来的）</param>
        public static Result Calculate(int completedToday, int dailyTaskCount, int dueTodayCount)
        {
            if (completedToday < 0) completedToday = 0;
            if (dailyTaskCount < 0) dailyTaskCount = 0;
            if (dueTodayCount < 0) dueTodayCount = 0;

            var planned = dailyTaskCount + dueTodayCount;
            // 比率 clamp：空计划为 0；超额时封顶 1（金色环另用 IsOverachieving）
            var rate = planned == 0 ? 0 : Math.Clamp((double)completedToday / planned, 0, 1);
            var over = completedToday > planned;
            var text = over
                ? $"{completedToday} / {planned} ✨"
                : $"{completedToday} / {planned}";
            return new Result(completedToday, planned, rate, over, text);
        }
    }
}
