using System;
using System.Collections.Generic;
using System.Windows;
using CommunityToolkit.WinUI.Notifications;
using TodoSidebar.Models;

namespace TodoSidebar.Services
{
    /// <summary>
    /// v5.6 交互式 Windows 原生 Toast：
    ///   到期通知带「完成 / 稍后提醒」按钮，无需打开应用即可操作；
    ///   应用未运行时点击按钮会拉起进程，激活事件在 App 启动最早期被接管。
    /// 失败静默回退到应用内通知窗（NotificationWindow）。
    /// </summary>
    public static class ToastService
    {
        /// <summary>激活参数键值约定：action=complete|snooze&amp;taskId=N</summary>
        private const string ArgAction = "action";
        private const string ArgTaskId = "taskId";
        private const string ActionComplete = "complete";
        private const string ActionSnooze = "snooze";

        private static bool _handlerRegistered;

        /// <summary>
        /// 注册激活事件处理器。必须在 App.OnStartup 的最早期调用——
        /// 从通知点击冷启动时，Toolkit 在订阅瞬间同步派发排队中的激活，
        /// 此后才会走完常规启动流程（含单实例互斥）。
        /// </summary>
        public static void EnsureActivatedHandler()
        {
            if (_handlerRegistered) return;
            _handlerRegistered = true;

            try
            {
                ToastNotificationManagerCompat.OnActivated += OnToastActivated;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Toast] 注册激活处理器失败: {ex.Message}");
            }
        }

        private static void OnToastActivated(ToastNotificationActivatedEventArgsCompat e)
        {
            try
            {
                var args = ParseArguments(e.Argument ?? "");
                var action = args.GetValueOrDefault(ArgAction);
                if (!int.TryParse(args.GetValueOrDefault(ArgTaskId), out var taskId) || taskId <= 0) return;

                switch (action)
                {
                    case ActionComplete:
                        CompleteFromActivation(taskId);
                        break;
                    case ActionSnooze:
                        SnoozeFromActivation(taskId);
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Toast] 激活处理失败: {ex.Message}");
            }
        }

        // ==================== v5.6 审查修复：第二实例激活转发 ====================
        // 场景：应用已在运行，用户点 Toast 按钮 → Windows 拉起第二进程 → 单实例互斥命中。
        // 此时让第二进程把激活参数写盘后静默退出，主进程的 1 分钟检查器读取并执行，
        // 避免"在将死的进程里直连 SQLite / 操作丢失"。

        private static readonly string PendingFilePath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TodoSidebar", "toast_pending.json");

        /// <summary>
        /// 从命令行参数中提取 Toast 激活段并写入待处理文件。
        /// 命中有效动作返回 true（调用方据此跳过"已运行"提示框静默退出）。
        /// </summary>
        public static bool TryForwardPending(string[] launchArgs)
        {
            try
            {
                var joined = string.Join(" ", launchArgs ?? Array.Empty<string>());
                var m = System.Text.RegularExpressions.Regex.Match(joined,
                    @"action=(complete|snooze).{0,40}?taskId=(\d+)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!m.Success) return false;

                var payload = System.Text.Json.JsonSerializer.Serialize(new PendingToast
                {
                    Action = m.Groups[1].Value.ToLowerInvariant(),
                    TaskId = int.Parse(m.Groups[2].Value),
                    StampUtc = DateTime.UtcNow.ToString("O")
                });
                var dir = System.IO.Path.GetDirectoryName(PendingFilePath)!;
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(PendingFilePath, payload);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Toast] 转发挂起失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>主进程检查器调用：读取并执行挂起激活（UI 线程），随后删除文件。</summary>
        public static void ProcessPendingActivations()
        {
            try
            {
                if (!System.IO.File.Exists(PendingFilePath)) return;
                var payload = System.Text.Json.JsonSerializer.Deserialize<PendingToast>(
                    System.IO.File.ReadAllText(PendingFilePath));
                System.IO.File.Delete(PendingFilePath);
                if (payload == null || payload.TaskId <= 0) return;

                switch (payload.Action)
                {
                    case ActionComplete: CompleteFromActivation(payload.TaskId); break;
                    case ActionSnooze: SnoozeFromActivation(payload.TaskId); break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Toast] 处理挂起激活失败: {ex.Message}");
            }
        }

        private sealed class PendingToast
        {
            public string Action { get; set; } = "";
            public int TaskId { get; set; }
            public string StampUtc { get; set; } = "";
        }

        private static void CompleteFromActivation(int taskId)
        {
            // 可能在非 UI 线程触发：DB 层自带锁安全；奖励/UI 刷新统一投递到 UI 线程
            Application.Current?.Dispatcher.Invoke(() =>
            {
                try
                {
                    var task = DatabaseService.Instance.GetTaskById(taskId);
                    if (task == null || task.IsCompleted || task.IsTodayCompleted) return;
                    TaskServiceHolder.Complete(task);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Toast] 完成任务失败: {ex.Message}");
                }
            });
        }

        private static void SnoozeFromActivation(int taskId)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                try { NotificationService.Instance.Snooze(taskId, TimeSpan.FromMinutes(30)); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Toast] 稍后提醒失败: {ex.Message}"); }
            });
        }

        /// <summary>发送任务交互 Toast；任何失败返回 false（调用方回退应用内通知）。</summary>
        public static bool TrySendTaskToast(int taskId, string title, string message)
        {
            try
            {
                new ToastContentBuilder()
                    .AddText(title)
                    .AddText(message)
                    .AddButton(new ToastButton()
                        .SetContent("✔ 完成")
                        .AddArgument(ArgAction, ActionComplete)
                        .AddArgument(ArgTaskId, taskId))
                    .AddButton(new ToastButton()
                        .SetContent("⏰ 稍后 30 分")
                        .AddArgument(ArgAction, ActionSnooze)
                        .AddArgument(ArgTaskId, taskId))
                    .Show();
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Toast] 发送失败（回退内置窗口）: {ex.Message}");
                return false;
            }
        }

        private static Dictionary<string, string> ParseArguments(string raw)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                if (kv.Length == 2) result[kv[0].Trim()] = kv[1].Trim();
            }
            return result;
        }

        /// <summary>延迟初始化的 TaskService 持有者（避免静态构造顺序问题）。</summary>
        private static class TaskServiceHolder
        {
            private static TaskService? _instance;
            public static void Complete(TaskItem task)
            {
                _instance ??= new TaskService(DatabaseService.Instance);
                _instance.CompleteTask(task);
            }
        }
    }
}
