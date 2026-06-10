using System;
using System.Threading.Tasks;

namespace TodoSidebar.Services
{
    /// <summary>
    /// 认证服务接口。
    /// AuthResult / SessionData 已在 AuthService.cs 中定义。
    /// </summary>
    public interface IAuthService
    {
        /// <summary>当前登录用户（Supabase User 对象）</summary>
        Supabase.Gotrue.User? CurrentUser { get; }

        /// <summary>当前用户 ID（未登录返回 null）</summary>
        string? CurrentUserId => CurrentUser?.Id;

        /// <summary>当前用户邮箱（未登录返回 null）</summary>
        string? CurrentEmail => CurrentUser?.Email;

        /// <summary>是否已登录</summary>
        bool IsLoggedIn { get; }

        /// <summary>初始化认证（恢复 session）</summary>
        Task InitializeAsync();

        /// <summary>邮箱密码登录</summary>
        Task<AuthResult> LoginWithEmailPasswordAsync(string email, string password);

        /// <summary>邮箱注册</summary>
        Task<AuthResult> SignUpWithEmailPasswordAsync(string email, string password);

        /// <summary>退出登录</summary>
        Task LogoutAsync();

        /// <summary>重置密码</summary>
        Task<AuthResult> ResetPasswordAsync(string email);

        /// <summary>登录状态变化事件（bool = isLoggedIn）</summary>
        event EventHandler<bool>? LoginStateChanged;
    }
}
