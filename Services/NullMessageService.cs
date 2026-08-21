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
            // L4 修复：无 UI 环境下无法让用户确认，拒绝破坏性操作比默认自动放行更安全
            System.Diagnostics.Debug.WriteLine($"[NullMessageService] Confirmation: {title} - {message} (auto-rejected)");
            return false;
        }
    }
}
