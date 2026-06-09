using System;

namespace TodoSidebar.Config
{
    /// <summary>
    /// Supabase 配置
    /// </summary>
    public static class SupabaseConfig
    {
        // Supabase 项目配置
        public static string Url { get; set; } = "https://rtszvchilzhcgdvlopdi.supabase.co";
        
        // 匿名密钥 (anon key)
        public static string AnonKey { get; set; } = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InJ0c3p2Y2hpbHpoY2dkdmxvcGRpIiwicm9sZSI6ImFub24iLCJpYXQiOjE3ODA5NzE0NzIsImV4cCI6MjA5NjU0NzQ3Mn0.-odGCeEO4YRb93jVJFiOo5ZZctYOva1qwK8pZCVtEHU";
        
        // 是否自动刷新 Token
        public static bool AutoRefreshToken { get; set; } = true;
        
        // 同步间隔（秒）
        public static int SyncIntervalSeconds { get; set; } = 30;
        
        // 离线队列最大长度
        public static int MaxOfflineQueueSize { get; set; } = 1000;
    }
}
