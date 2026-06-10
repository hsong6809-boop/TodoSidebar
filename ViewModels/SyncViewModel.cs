using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TodoSidebar.Services;

namespace TodoSidebar.ViewModels
{
    /// <summary>
    /// 同步操作 ViewModel。
    /// 从 MainViewModel 提取，负责云同步的 UI 状态和命令。
    /// </summary>
    public partial class SyncViewModel : ObservableObject
    {
        private readonly SyncService _syncService;
        private readonly IMessageService _messageService;

        [ObservableProperty]
        private bool _isSyncing;
        
        [ObservableProperty]
        private string _syncStatusText = string.Empty;
        
        [ObservableProperty]
        private DateTime? _lastSyncTime;

        /// <summary>
        /// 同步完成后的回调（用于刷新主界面数据）
        /// </summary>
        public Action? OnSyncCompleted { get; set; }

        public SyncViewModel(SyncService syncService, IMessageService messageService)
        {
            _syncService = syncService;
            _messageService = messageService;
        }

        [RelayCommand]
        private async Task SyncAllAsync()
        {
            if (!AuthService.Instance.IsLoggedIn)
            {
                _messageService.ShowWarning("请先登录后再同步", "未登录");
                return;
            }

            IsSyncing = true;
            SyncStatusText = "正在同步...";

            try
            {
                var result = await _syncService.SyncAsync();
                
                if (result.Success)
                {
                    LastSyncTime = DateTime.Now;
                    SyncStatusText = $"同步完成：上传 {result.Uploaded} 条，下载 {result.Downloaded} 条";
                    OnSyncCompleted?.Invoke();
                }
                else
                {
                    SyncStatusText = $"同步失败：{result.Error}";
                    _messageService.ShowError($"同步失败：{result.Error}", "同步错误");
                }
            }
            catch (Exception ex)
            {
                SyncStatusText = $"同步出错：{ex.Message}";
                _messageService.ShowError($"同步出错：{ex.Message}", "同步错误");
            }
            finally
            {
                IsSyncing = false;
            }
        }

        [RelayCommand]
        private async Task UploadAsync()
        {
            if (!AuthService.Instance.IsLoggedIn)
            {
                _messageService.ShowWarning("请先登录后再上传", "未登录");
                return;
            }

            IsSyncing = true;
            SyncStatusText = "正在上传本地数据...";

            try
            {
                var uploaded = await _syncService.UploadLocalChangesAsync();
                LastSyncTime = DateTime.Now;
                SyncStatusText = $"上传完成：{uploaded} 条数据已上传";
                _messageService.ShowMessage($"成功上传 {uploaded} 条数据到云端", "上传完成");
            }
            catch (Exception ex)
            {
                SyncStatusText = $"上传出错：{ex.Message}";
                _messageService.ShowError($"上传出错：{ex.Message}", "上传错误");
            }
            finally
            {
                IsSyncing = false;
            }
        }

        [RelayCommand]
        private async Task DownloadAsync()
        {
            if (!AuthService.Instance.IsLoggedIn)
            {
                _messageService.ShowWarning("请先登录后再下载", "未登录");
                return;
            }

            IsSyncing = true;
            SyncStatusText = "正在从云端下载数据...";

            try
            {
                var downloaded = await _syncService.DownloadRemoteChangesAsync();
                LastSyncTime = DateTime.Now;
                SyncStatusText = $"下载完成：{downloaded} 条数据已下载";
                OnSyncCompleted?.Invoke();
                _messageService.ShowMessage($"成功从云端下载 {downloaded} 条数据", "下载完成");
            }
            catch (Exception ex)
            {
                SyncStatusText = $"下载出错：{ex.Message}";
                _messageService.ShowError($"下载出错：{ex.Message}", "下载错误");
            }
            finally
            {
                IsSyncing = false;
            }
        }
    }
}
