using System;
using System.IO;
using System.Text.Json;

namespace TodoSidebar.Config
{
    /// <summary>
    /// Supabase 配置。
    /// 优先级：环境变量 > AppData/config.json > 硬编码默认值。
    /// ⚠️ 安全提示：不要将真实的 Supabase URL 和 Anon Key 提交到公开仓库。
    /// 建议通过环境变量 SUPABASE_URL 和 SUPABASE_ANON_KEY 配置。
    /// </summary>
    public static class SupabaseConfig
    {
        private static bool _loaded = false;

        // 占位符 —— 真实配置必须通过环境变量或 AppData/TodoSidebar/supabase.json 提供！
        // 未配置时 GetConfigError() 返回错误说明，避免应用静默使用无效凭据。
        private static string _url = string.Empty;
        private static string _anonKey = string.Empty;

        public static string Url
        {
            get { EnsureLoaded(); return _url; }
            set { _url = value; }
        }

        public static string AnonKey
        {
            get { EnsureLoaded(); return _anonKey; }
            set { _anonKey = value; }
        }

        /// <summary>
        /// 配置是否完整（URL 和 AnonKey 均非空）。
        /// 启动时调用，缺失时提示用户配置。
        /// </summary>
        public static bool IsConfigured
        {
            get
            {
                EnsureLoaded();
                return !string.IsNullOrWhiteSpace(_url) && !string.IsNullOrWhiteSpace(_anonKey);
            }
        }

        /// <summary>
        /// 获取配置缺失的错误说明（用于启动引导）。
        /// </summary>
        public static string GetConfigError()
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(_url) && string.IsNullOrWhiteSpace(_anonKey))
                return "未配置 Supabase URL 和 AnonKey。请设置环境变量 SUPABASE_URL / SUPABASE_ANON_KEY，"
                     + "或在 %AppData%\\TodoSidebar\\supabase.json 中配置。";
            if (string.IsNullOrWhiteSpace(_url))
                return "未配置 Supabase URL。请设置环境变量 SUPABASE_URL 或在 supabase.json 中配置。";
            if (string.IsNullOrWhiteSpace(_anonKey))
                return "未配置 Supabase AnonKey。请设置环境变量 SUPABASE_ANON_KEY 或在 supabase.json 中配置。";
            return string.Empty;
        }

        public static bool AutoRefreshToken { get; set; } = true;
        public static int SyncIntervalSeconds { get; set; } = 30;

        /// <summary>
        /// 确保配置已从外部源加载（只执行一次）。
        /// </summary>
        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            // 1. 环境变量优先
            var envUrl = Environment.GetEnvironmentVariable("SUPABASE_URL");
            var envKey = Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY");
            if (!string.IsNullOrEmpty(envUrl)) _url = envUrl;
            if (!string.IsNullOrEmpty(envKey)) _anonKey = envKey;

            // 2. AppData/config.json
            try
            {
                var configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "TodoSidebar", "supabase.json");

                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    var config = JsonSerializer.Deserialize<SupabaseConfigFile>(json);
                    if (config != null)
                    {
                        if (!string.IsNullOrEmpty(config.Url)) _url = config.Url;
                        if (!string.IsNullOrEmpty(config.AnonKey)) _anonKey = config.AnonKey;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SupabaseConfig load error: {ex.Message}");
            }
        }

        /// <summary>
        /// 重新加载配置（用于测试或运行时刷新）。
        /// </summary>
        public static void Reload()
        {
            _loaded = false;
        }

        private class SupabaseConfigFile
        {
            public string? Url { get; set; }
            public string? AnonKey { get; set; }
        }
    }
}
