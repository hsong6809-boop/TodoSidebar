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
        /// 释放客户端
        /// </summary>
        public static void Dispose()
        {
            _client = null;
        }
    }
}
