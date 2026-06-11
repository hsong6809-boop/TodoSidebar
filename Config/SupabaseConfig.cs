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

        private static string _url = "https://rtszvchilzhcgdvlopdi.supabase.co";
        private static string _anonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InJ0c3p2Y2hpbHpoY2dkdmxvcGRpIiwicm9sZSI6ImFub24iLCJpYXQiOjE3ODA5NzE0NzIsImV4cCI6MjA5NjU0NzQ3Mn0.-odGCeEO4YRb93jVJFiOo5ZZctYOva1qwK8pZCVtEHU";

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
