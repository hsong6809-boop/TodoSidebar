using System;
using System.IO;
using System.Text.Json;

namespace TodoSidebar.Config
{
    /// <summary>
    /// Supabase 配置。
    /// 优先级：环境变量 > AppData/config.json。
    /// ⚠️ 安全提示：不要将真实的 Supabase URL 和 Anon Key 提交到仓库。
    /// 必须通过环境变量 SUPABASE_URL / SUPABASE_ANON_KEY 或
    /// %APPDATA%\TodoSidebar\supabase.json 配置，未配置时启动即报错。
    /// </summary>
    public static class SupabaseConfig
    {
        private static readonly object _configLock = new object();
        private static bool _loaded = false;

        // 不再内置默认凭据：缺失配置时明确报错（fail-fast），防止匿名凭据泄露
        private static string? _url;
        private static string? _anonKey;

        public static string Url
        {
            get { EnsureLoaded(); return _url ?? throw new InvalidOperationException("Supabase URL 未配置：请设置环境变量 SUPABASE_URL 或 %APPDATA%\\TodoSidebar\\supabase.json"); }
            set { _url = value; }
        }

        public static string AnonKey
        {
            get { EnsureLoaded(); return _anonKey ?? throw new InvalidOperationException("Supabase Anon Key 未配置：请设置环境变量 SUPABASE_ANON_KEY 或 %APPDATA%\\TodoSidebar\\supabase.json"); }
            set { _anonKey = value; }
        }

        public static bool AutoRefreshToken { get; set; } = true;
        public static int SyncIntervalSeconds { get; } = 30;

        /// <summary>
        /// 确保配置已从外部源加载（线程安全，只执行一次）。
        /// </summary>
        private static void EnsureLoaded()
        {
            if (_loaded) return;

            lock (_configLock)
            {
                if (_loaded) return;
                _loaded = true;

                // 1. 环境变量优先
                var envUrl = Environment.GetEnvironmentVariable("SUPABASE_URL");
                var envKey = Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY");
                if (!string.IsNullOrEmpty(envUrl)) _url = envUrl;
                if (!string.IsNullOrEmpty(envKey)) _anonKey = envKey;

                // 2. 配置文件（AppData 优先，其次 exe 同目录，后者可覆盖前者）
                try
                {
                    var candidates = new[]
                    {
                        Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            "TodoSidebar", "supabase.json"),
                        Path.Combine(AppContext.BaseDirectory, "supabase.json")
                    };

                    foreach (var configPath in candidates)
                    {
                        if (!File.Exists(configPath)) continue;

                        var json = File.ReadAllText(configPath);
                        var config = JsonSerializer.Deserialize<SupabaseConfigFile>(json);
                        if (config == null) continue;

                        if (!string.IsNullOrEmpty(config.Url)) _url = config.Url;
                        if (!string.IsNullOrEmpty(config.AnonKey)) _anonKey = config.AnonKey;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SupabaseConfig load error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 重新加载配置（用于测试或运行时刷新）。
        /// </summary>
        public static void Reload()
        {
            lock (_configLock)
            {
                _loaded = false;
            }
        }

        private class SupabaseConfigFile
        {
            public string? Url { get; set; }
            public string? AnonKey { get; set; }
        }
    }
}
