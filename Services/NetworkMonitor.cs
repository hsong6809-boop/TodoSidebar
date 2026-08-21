using System;
using System.Net.NetworkInformation;
using System.Threading;

namespace TodoSidebar.Services
{
    /// <summary>
    /// 网络状态监控。
    /// 监听网络变化事件，提供在线/离线状态查询。
    /// </summary>
    public class NetworkMonitor : IDisposable
    {
        // Lazy 线程安全单例，避免首访并发创建双实例导致事件双重订阅
        private static readonly Lazy<NetworkMonitor> _lazy = new(() => new NetworkMonitor());
        public static NetworkMonitor Instance => _lazy.Value;

        // L29 修复：bool 的 check-then-set 非原子，改用 int + Interlocked（0=离线，1=在线）
        private int _isOnline;

        /// <summary>当前是否在线</summary>
        public bool IsOnline => Volatile.Read(ref _isOnline) != 0;

        /// <summary>离线开始时间</summary>
        public DateTime? OfflineSince { get; private set; }

        /// <summary>网络状态变化事件</summary>
        public event EventHandler<bool>? ConnectivityChanged;

        private NetworkMonitor()
        {
            _isOnline = NetworkInterface.GetIsNetworkAvailable() ? 1 : 0;
            if (_isOnline == 0)
                OfflineSince = DateTime.Now;

            NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
            NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
        }

        private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
        {
            UpdateStatus(e.IsAvailable);
        }

        private void OnNetworkAddressChanged(object? sender, EventArgs e)
        {
            var available = NetworkInterface.GetIsNetworkAvailable();
            UpdateStatus(available);
        }

        private void UpdateStatus(bool online)
        {
            // L29 修复：Interlocked.Exchange 原子交换并用旧值判断状态是否翻转，
            // 并发网络回调下不会重复触发或丢失 ConnectivityChanged 事件
            var newValue = online ? 1 : 0;
            var oldValue = Interlocked.Exchange(ref _isOnline, newValue);
            if (oldValue == newValue) return;

            if (!online)
            {
                OfflineSince = DateTime.Now;
                System.Diagnostics.Debug.WriteLine("[NetworkMonitor] 已离线");
            }
            else
            {
                var offlineDuration = OfflineSince.HasValue
                    ? DateTime.Now - OfflineSince.Value
                    : TimeSpan.Zero;
                OfflineSince = null;
                System.Diagnostics.Debug.WriteLine($"[NetworkMonitor] 已恢复在线（离线 {offlineDuration.TotalMinutes:F0} 分钟）");
            }

            ConnectivityChanged?.Invoke(this, online);
        }

        /// <summary>
        /// 离线时长
        /// </summary>
        public TimeSpan OfflineDuration =>
            OfflineSince.HasValue ? DateTime.Now - OfflineSince.Value : TimeSpan.Zero;

        public void Dispose()
        {
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
            NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        }
    }
}
