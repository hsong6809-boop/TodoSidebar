namespace TodoSidebar.Services
{
    /// <summary>
    /// 消息服务接口，用于解耦 ViewModel 和 UI
    /// </summary>
    public interface IMessageService
    {
        void ShowMessage(string message, string title = "提示");
        void ShowWarning(string message, string title = "警告");
        void ShowError(string message, string title = "错误");
        bool ShowConfirmation(string message, string title = "确认");
    }
}
