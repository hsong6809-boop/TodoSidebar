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
        private static NetworkMonitor? _instance;
        public static NetworkMonitor Instance => _instance ??= new NetworkMonitor();

        /// <summary>当前是否在线</summary>
        public bool IsOnline { get; private set; } = true;

        /// <summary>离线开始时间</summary>
        public DateTime? OfflineSince { get; private set; }

        /// <summary>网络状态变化事件</summary>
        public event EventHandler<bool>? ConnectivityChanged;

        private NetworkMonitor()
        {
            IsOnline = NetworkInterface.GetIsNetworkAvailable();
            if (!IsOnline)
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
            if (IsOnline == online) return;

            IsOnline = online;
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
