using System;
using System.Windows;

namespace TodoSidebar.Services
{
    // IMessageService 接口已移至 TodoSidebar.Core/Interfaces/IMessageService.cs

    /// <summary>
    /// 消息服务实现
    /// </summary>
    public class MessageService : IMessageService
    {
        private static MessageService? _instance;
        public static MessageService Instance => _instance ??= new MessageService();

        public void ShowMessage(string message, string title = "提示")
        {
            // 异步封送，避免阻塞调用方（如后台线程）
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
            });
        }

        public void ShowWarning(string message, string title = "警告")
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
            });
        }

        public void ShowError(string message, string title = "错误")
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }

        /// <summary>
        /// 显示是/否确认对话框并返回用户选择。
        /// L4 修复：内部使用 Dispatcher.Invoke 同步封送，存在死锁窗口——仅限 UI 线程调用，
        /// 不要在持有 UI 线程所等资源（锁/任务结果）的后台线程上调用。
        /// </summary>
        public bool ShowConfirmation(string message, string title = "确认")
        {
            return Application.Current?.Dispatcher.Invoke(() =>
            {
                return MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
            }) ?? false;
        }
    }
}
