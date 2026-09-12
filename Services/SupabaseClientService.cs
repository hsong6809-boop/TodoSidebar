using System;
using System.Threading.Tasks;
using Supabase;
using TodoSidebar.Config;

namespace TodoSidebar.Services
{
    /// <summary>
    /// Supabase 客户端服务
    /// </summary>
    public static class SupabaseClientService
    {
        private static Supabase.Client? _client;
        private static readonly object _lock = new object();
        
        /// <summary>
        /// 获取 Supabase 客户端实例
        /// </summary>
        public static Supabase.Client Client
        {
            get
            {
                if (_client == null)
                {
                    lock (_lock)
                    {
                        if (_client == null)
                        {
                            _client = CreateClient();
                        }
                    }
                }
                return _client;
            }
        }
        
        /// <summary>
        /// 是否已初始化
        /// </summary>
        public static bool IsInitialized => _client != null;
        
        /// <summary>
        /// 初始化 Supabase 客户端
        /// </summary>
        public static async Task InitializeAsync()
        {
            if (_client != null)
                return;
                
            lock (_lock)
            {
                if (_client == null)
                {
                    _client = CreateClient();
                }
            }
            
            await Task.CompletedTask;
        }
        
        /// <summary>
        /// 创建 Supabase 客户端
        /// </summary>
        private static Supabase.Client CreateClient()
        {
            var options = new SupabaseOptions
            {
                AutoRefreshToken = SupabaseConfig.AutoRefreshToken
            };
            
            return new Supabase.Client(SupabaseConfig.Url, SupabaseConfig.AnonKey, options);
        }
        
        /// <summary>
        /// 释放客户端（加锁与 Client getter 互斥，避免竞态重建）
        /// </summary>
        public static void Dispose()
        {
            lock (_lock)
            {
                // R71（复审 M13）：先释放旧 Client（其内部 HttpClient / 自动刷新定时器），
                // 再置空。原实现只置 null，切号时旧连接与定时器泄漏。
                try { (_client as IDisposable)?.Dispose(); }
                catch { /* 释放失败不影响置空 */ }
                _client = null;
            }
        }
    }
}
