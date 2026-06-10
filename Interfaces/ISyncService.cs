using System;
using System.Threading.Tasks;
using TodoSidebar.Models;

namespace TodoSidebar.Services
{
    /// <summary>
    /// 云同步服务接口。
    /// SyncStatus / SyncResult 已定义在 Models/SyncModels.cs。
    /// </summary>
    public interface ISyncService
    {
        /// <summary>同步状态</summary>
        SyncStatus Status { get; }

        /// <summary>最后同步时间</summary>
        DateTime? LastSyncTime { get; }

        /// <summary>设置 Feature Flag 服务</summary>
        void SetFeatureFlags(IFeatureFlagService featureFlags);

        /// <summary>初始化同步服务（启动定时同步）</summary>
        Task InitializeAsync();

        /// <summary>执行一次完整同步（上传+下载）</summary>
        Task<SyncResult> SyncAsync();

        /// <summary>仅上传本地变更</summary>
        Task<int> UploadLocalChangesAsync();

        /// <summary>仅下载远程变更</summary>
        Task<(int downloaded, int conflicts)> DownloadRemoteChangesAsync();

        /// <summary>手动同步（不检查 FeatureFlag）</summary>
        Task<SyncResult> ManualSyncAsync();

        /// <summary>停止同步服务</summary>
        void Stop();

        /// <summary>同步状态变化事件</summary>
        event EventHandler<SyncStatus>? StatusChanged;

        /// <summary>同步完成事件</summary>
        event EventHandler<SyncResult>? SyncCompleted;
    }
}
