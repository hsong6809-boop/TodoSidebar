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
            Application.Current?.Dispatcher.Invoke(() =>
            {
                MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
            });
        }

        public void ShowWarning(string message, string title = "警告")
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
            });
        }

        public void ShowError(string message, string title = "错误")
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }

        public bool ShowConfirmation(string message, string title = "确认")
        {
            return Application.Current?.Dispatcher.Invoke(() =>
            {
                return MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
            }) ?? false;
        }
    }
}
