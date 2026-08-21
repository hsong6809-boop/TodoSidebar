using System;
using System.Windows.Threading;
using TodoSidebar.Models;

namespace TodoSidebar.Services
{
    /// <summary>
    /// 番茄钟状态
    /// </summary>
    public enum PomodoroState
    {
        Idle,       // 未开始
        Focus,      // 专注中
        Paused,     // 已暂停
        Break       // 休息中
    }

    /// <summary>
    /// 番茄会话完成事件参数
    /// </summary>
    public class PomodoroSessionCompletedEventArgs : EventArgs
    {
        public int? TaskId { get; }
        public bool Completed { get; }
        public int DurationMinutes { get; }
        public bool EstimatedReached { get; }

        public PomodoroSessionCompletedEventArgs(int? taskId, bool completed, int durationMinutes, bool estimatedReached)
        {
            TaskId = taskId;
            Completed = completed;
            DurationMinutes = durationMinutes;
            EstimatedReached = estimatedReached;
        }
    }

    /// <summary>
    /// 番茄钟服务（升级系统 P2）。
    /// 状态机：Idle → Focus → (Pause ⇄ Focus) → 完成(Break) 或 中断(Idle)。
    /// 完成番茄：写会话、回写任务 ActualMinutes、发放经验（防刷：中断不计 XP）。
    /// </summary>
    public class PomodoroService
    {
        private static readonly Lazy<PomodoroService> _lazy = new(() => new PomodoroService());
        public static PomodoroService Instance => _lazy.Value;

        public const int FocusMinutesDefault = 25;
        public const int ShortBreakMinutes = 5;
        public const int LongBreakMinutes = 15;
        public const int RoundsPerCycle = 4;
        public const int DailyTarget = 4;

        /// <summary>M16 修复：有效完成的最短专注秒数，低于此值强制按中断处理（防秒刷经验）</summary>
        public const int MinFocusSeconds = 60;

        private readonly DatabaseService _db;
        private readonly TaskService _taskService;
        private readonly DispatcherTimer _timer;

        public PomodoroState State { get; private set; } = PomodoroState.Idle;
        public int RemainingSeconds { get; private set; }
        public int TotalSeconds { get; private set; }
        public int? BoundTaskId { get; private set; }
        public string BoundTaskTitle { get; private set; } = "";
        private DateTime _sessionStart;

        /// <summary>每秒跳动（UI 刷新用）</summary>
        public event EventHandler? Tick;

        /// <summary>状态变化</summary>
        public event EventHandler<PomodoroState>? StateChanged;

        /// <summary>会话结束（完成或中断）</summary>
        public event EventHandler<PomodoroSessionCompletedEventArgs>? SessionCompleted;

        private PomodoroService()
        {
            _db = DatabaseService.Instance;
            _taskService = new TaskService(_db);

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += (s, e) => OnTimerTick();
        }

        /// <summary>
        /// 今日已完成番茄数（从数据库实时读取）。
        /// </summary>
        public (int completed, int interrupted, int focusMinutes) GetTodayStats()
        {
            var date = DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture); // L7 修复：InvariantCulture 防区域差异
            var (completed, interrupted) = _db.GetPomodoroCountByDate(date);
            var minutes = _db.GetFocusMinutesByDate(date);
            return (completed, interrupted, minutes);
        }

        /// <summary>
        /// 开始一个番茄（可选绑定任务）。
        /// </summary>
        public void Start(int? taskId = null, string taskTitle = "", int minutes = FocusMinutesDefault)
        {
            if (State == PomodoroState.Focus || State == PomodoroState.Paused)
                return;

            // L8 修复说明：Break 状态走到这里即"静默结束休息"——不结算任何会话，
            // 直接覆盖为新的专注参数进入 Focus（计时器本就在跑且间隔不变，无需额外处理），
            // 用户不必干等休息结束就能开始下一个番茄。
            BoundTaskId = taskId;
            BoundTaskTitle = taskTitle ?? "";
            TotalSeconds = Math.Max(1, minutes) * 60;
            RemainingSeconds = TotalSeconds;
            _sessionStart = DateTime.Now;

            SetState(PomodoroState.Focus);
            _timer.Start();
        }

        public void Pause()
        {
            if (State != PomodoroState.Focus) return;
            _timer.Stop();
            SetState(PomodoroState.Paused);
        }

        public void Resume()
        {
            if (State != PomodoroState.Paused) return;
            _timer.Start();
            SetState(PomodoroState.Focus);
        }

        /// <summary>
        /// 停止当前番茄。complete=false 视为中断（不计 XP）。
        /// M16 修复：请求完成但实际专注时长不足 MinFocusSeconds 时强制按中断处理。
        /// L8 修复：休息中调用则直接取消休息回 Idle，不结算任何会话。
        /// </summary>
        public void Stop(bool complete)
        {
            // L8 修复：Break（休息中）也允许停止——此前休息中既不能 Stop 也不能 Pause，只能干等结束
            if (State == PomodoroState.Break)
            {
                _timer.Stop();
                // 休息中直接回 Idle，不结算任何会话；补发一次 Tick 让 UI 刷新到待机显示
                SetState(PomodoroState.Idle);
                Tick?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (State != PomodoroState.Focus && State != PomodoroState.Paused)
                return;

            _timer.Stop();

            if (complete && (TotalSeconds - RemainingSeconds) < MinFocusSeconds)
                complete = false;

            FinishSession(complete);
            SetState(PomodoroState.Idle);
        }

        private void OnTimerTick()
        {
            if (RemainingSeconds <= 0)
            {
                _timer.Stop();

                // 休息结束：回到待机，不结算会话
                if (State == PomodoroState.Break)
                {
                    SetState(PomodoroState.Idle);
                    Tick?.Invoke(this, EventArgs.Empty);
                    return;
                }

                // 专注结束：结算完成会话，然后进入休息
                FinishSession(complete: true);
                SetState(PomodoroState.Break);

                int breakMinutes = (_db.GetPomodoroCountByDate(DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)).completed % RoundsPerCycle == 0) // L7 修复：InvariantCulture 防区域差异
                    ? LongBreakMinutes : ShortBreakMinutes;
                RemainingSeconds = breakMinutes * 60;
                TotalSeconds = RemainingSeconds;
                _timer.Start();
                Tick?.Invoke(this, EventArgs.Empty);
                return;
            }

            RemainingSeconds--;
            Tick?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 会话结算：写库 + 回写任务时长 + 发经验（仅完成时）。
        /// </summary>
        private void FinishSession(bool complete)
        {
            // M15 修复：专注时长用计时器推导（TotalSeconds - RemainingSeconds），
            // 暂停期间计时器已停、RemainingSeconds 不变，天然排除暂停时长；
            // 原实现按墙钟差计算，暂停 2 小时会把 145 分钟记成专注时长
            var focusedSeconds = Math.Max(0, TotalSeconds - RemainingSeconds);
            var minutes = Math.Max(1, (int)Math.Round(focusedSeconds / 60.0));
            var date = DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture); // L7 修复：InvariantCulture 防区域差异

            _db.AddPomodoroSession(BoundTaskId, _sessionStart, DateTime.Now, minutes, complete, date);

            bool estimatedReached = false;
            if (complete)
            {
                // 回写绑定任务的 ActualMinutes
                if (BoundTaskId.HasValue)
                {
                    var task = _db.GetTaskById(BoundTaskId.Value);
                    if (task != null && !task.IsCompleted)
                    {
                        task.ActualMinutes = (task.ActualMinutes ?? 0) + minutes;
                        _db.UpdateTask(task);
                        estimatedReached = task.EstimatedMinutes.HasValue && task.ActualMinutes >= task.EstimatedMinutes.Value;
                    }
                }

                // 发经验：绑定任务 +10（5 基础 + 5 联动），未绑定 +5。
                // M17 修复：taskId 传 null——"pomodoro" 在 RepeatableSources 白名单中按会话独立计奖，
                // 原实现传 BoundTaskId 会命中（同任务同天）防重，导致第 2+ 个番茄零经验
                var xp = BoundTaskId.HasValue ? 10 : 5;
                LevelService.Instance.Reward("pomodoro", xp, null);

                // 每日第 4 个番茄：整轮奖励
                var (completedToday, _, _) = GetTodayStats();
                if (completedToday == RoundsPerCycle)
                    LevelService.Instance.Reward("pomodoro_round", 15, null);

                // 每日目标（4 个）达成：一次性奖励。
                // S10 修复：LevelService.Reward 现在对 null-taskId 来源按（来源,日期）防重，
                // 第 5、6、7…个番茄不会再重复发放这 10 XP。
                if (completedToday >= DailyTarget)
                    LevelService.Instance.Reward("pomodoro_daily", 10, null);

                // 每日挑战进度推进（专注类挑战）
                DailyChallengeService.Instance.RegisterProgress("complete_pomodoros");

                // 成就检查（专注类/全神贯注徽章）
                AchievementService.Instance.CheckAll();
            }

            SessionCompleted?.Invoke(this, new PomodoroSessionCompletedEventArgs(BoundTaskId, complete, minutes, estimatedReached));
        }

        private void SetState(PomodoroState state)
        {
            State = state;
            StateChanged?.Invoke(this, state);
        }

        /// <summary>
        /// 格式化剩余时间 mm:ss。
        /// </summary>
        public static string FormatTime(int seconds) =>
            $"{seconds / 60:00}:{seconds % 60:00}";
    }
}
