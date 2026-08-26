using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace TodoSidebar.Services
{
    /// <summary>
    /// 应用更新检测（M40）：
    /// - 版本源：GitHub Releases latest（api.github.com），与本地程序集版本比较；
    /// - 每日首次启动静默检测（LastUpdateCheckDate 设置项做当日去重，失败也计入当天，
    ///   避免网络不通时每次启动都弹/都请求）；
    /// - 设置页提供手动检测（不受每日门控限制）；
    /// - 发现新版本弹确认框，确认后跳转浏览器打开 Release 下载页。
    /// 全程静默容错：无网络/GitHub 不可达时不打扰用户。
    /// </summary>
    public static class UpdateChecker
    {
        private const string LatestApiUrl = "https://api.github.com/repos/hsong6809-boop/TodoSidebar/releases/latest";
        private const string ReleasePageUrl = "https://github.com/hsong6809-boop/TodoSidebar/releases/latest";
        private const string LastCheckDateSetting = "LastUpdateCheckDate";
        private const int ChangelogMaxChars = 500;

        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            // GitHub API 强制要求 User-Agent
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TodoSidebar-UpdateChecker");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }

        /// <summary>更新信息</summary>
        public sealed class UpdateInfo
        {
            public bool HasUpdate;
            public string CurrentVersion = "";
            public string RemoteTag = "";
            public string RemoteVersion = "";
            public string Changelog = "";
            public string PageUrl = ReleasePageUrl;
        }

        /// <summary>
        /// 每日首次启动检测入口。同一自然天只请求一次（无论成败）。
        /// </summary>
        public static async Task RunDailyCheckAsync()
        {
            try
            {
                var today = DateTime.Today.ToString("yyyy-MM-dd");
                string last;
                try { last = DatabaseService.Instance.GetSetting(LastCheckDateSetting) ?? ""; }
                catch { return; } // 数据库不可用则不打扰

                if (last == today) return; // 今天已检测过

                // 先记账再检测：保证一天最多一次，失败也不反复打扰
                try { DatabaseService.Instance.SetSetting(LastCheckDateSetting, today); }
                catch { /* 写失败下次再试 */ }

                var info = await CheckAsync();
                if (info != null && info.HasUpdate)
                    PromptDownload(info);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateChecker] daily check error: {ex.Message}");
            }
        }

        /// <summary>
        /// 执行一次版本检测。返回 null 表示检测失败（网络/API 异常）。
        /// </summary>
        public static async Task<UpdateInfo?> CheckAsync()
        {
            try
            {
                using var response = await Http.GetAsync(LatestApiUrl);
                if (!response.IsSuccessStatusCode)
                    return null;

                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var root = doc.RootElement;

                var tagName = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? "" : "";
                var body = root.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() ?? "" : "";
                var pageUrl = root.TryGetProperty("html_url", out var urlEl) ? urlEl.GetString() : null;

                var current = Assembly.GetExecutingAssembly().GetName().Version;
                var currentShort = current == null ? "" : new Version(current.Major, current.Minor, Math.Max(current.Build, 0)).ToString(3);

                var remoteVer = ParseVersion(tagName);
                var info = new UpdateInfo
                {
                    CurrentVersion = currentShort,
                    RemoteTag = tagName,
                    // R51 修复（审查 M1）：按实际组件数格式化——原实现硬编码 ToString(3)，
                    // tag 只有 1~2 段（如 "v5.4"、"v6"）时抛 ArgumentException，
                    // 被外层 catch 吞掉 => 更新检测整体静默失效且无日志指向根因
                    RemoteVersion = FormatVersion(remoteVer) ?? tagName,
                    Changelog = body.Length > ChangelogMaxChars ? body.Substring(0, ChangelogMaxChars) + "…" : body,
                    PageUrl = string.IsNullOrEmpty(pageUrl) ? ReleasePageUrl : pageUrl
                };
                info.HasUpdate = remoteVer != null && current != null && remoteVer > new Version(current.Major, current.Minor, Math.Max(current.Build, 0));
                return info;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateChecker] check failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>解析 "v5.0.2" / "5.0.2" 形式的标签；失败返回 null。</summary>
        private static Version? ParseVersion(string tagName)
        {
            if (string.IsNullOrWhiteSpace(tagName)) return null;
            var cleaned = tagName.TrimStart('v', 'V').Trim();
            return Version.TryParse(cleaned, out var v) ? v : null;
        }

        /// <summary>
        /// R51：按版本实际组件数格式化。Version.ToString(fieldCount) 在
        /// fieldCount 大于组件数时抛 ArgumentException，必须先判 Build/Revision 是否有效。
        /// </summary>
        private static string? FormatVersion(Version? v)
        {
            if (v == null) return null;
            if (v.Revision >= 0) return v.ToString(4);
            if (v.Build >= 0) return v.ToString(3);
            return v.ToString(2);
        }

        /// <summary>
        /// 弹出升级确认框；用户确认后用系统默认浏览器打开 Release 下载页。
        /// 可在任意线程调用（内部调度到 UI 线程）。
        /// </summary>
        public static void PromptDownload(UpdateInfo info)
        {
            var app = Application.Current;
            if (app == null) return;

            app.Dispatcher.Invoke(() =>
            {
                var message = $"发现新版本 {info.RemoteVersion}（当前 {info.CurrentVersion}）";
                if (!string.IsNullOrWhiteSpace(info.Changelog))
                    message += $"\n\n———— 更新内容 ————\n{info.Changelog}";
                message += "\n\n是否前往下载页面？";

                var result = MessageBox.Show(message, "软件更新",
                    MessageBoxButton.YesNo, MessageBoxImage.Information,
                    MessageBoxResult.Yes);
                if (result == MessageBoxResult.Yes)
                    OpenReleasePage(info.PageUrl);
            });
        }

        private static void OpenReleasePage(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateChecker] open browser failed: {ex.Message}");
                // 浏览器打开失败时退回剪贴板方案，用户仍可手动访问
                try { Clipboard.SetText(url); } catch { }
            }
        }
    }
}
