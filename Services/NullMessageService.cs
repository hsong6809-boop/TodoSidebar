namespace TodoSidebar.Services
{
    /// <summary>
    /// 空实现的 MessageService，用于不需要 UI 提示的场景。
    /// DI 完成后将由真正的 MessageService 替代。
    /// </summary>
    internal class NullMessageService : IMessageService
    {
        public void ShowMessage(string message, string title = "提示") { }
        public void ShowWarning(string message, string title = "警告") { }
        public void ShowError(string message, string title = "错误") { }
        public bool ShowConfirmation(string message, string title = "确认")
        {
            // 无 UI 环境下默认允许操作，避免阻塞
            System.Diagnostics.Debug.WriteLine($"[NullMessageService] Confirmation: {title} - {message} (auto-accepted)");
            return true;
        }
    }
}
