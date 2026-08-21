using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Supabase.Gotrue;
using Supabase.Gotrue.Interfaces;
using static Supabase.Gotrue.Constants;
using TodoSidebar.Config;

namespace TodoSidebar.Services
{
    /// <summary>
    /// 认证服务
    /// </summary>
    public class AuthService : IAuthService
    {
        private static AuthService? _instance;
        private static readonly object _lock = new object();
        
        // Session 持久化文件路径
        private static readonly string SessionFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TodoSidebar", "session.json");

        /// <summary>session 文件读写锁（M9 修复：并发保存串行化）</summary>
        private static readonly object _sessionFileLock = new object();
        
        public static AuthService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new AuthService();
                        }
                    }
                }
                return _instance;
            }
        }
        
        /// <summary>
        /// 当前用户
        /// </summary>
        public User? CurrentUser { get; private set; }
        
        /// <summary>
        /// 是否已登录
        /// </summary>
        public bool IsLoggedIn => CurrentUser != null;
        
        /// <summary>
        /// 登录状态变化事件
        /// </summary>
        public event EventHandler<bool>? LoginStateChanged;
        
        private AuthService()
        {
        }
        
        /// <summary>
        /// 初始化认证服务
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                await SupabaseClientService.InitializeAsync();

                // 订阅 token 自动刷新事件：刷新后回写 session 文件，避免重启后使用过期 refresh token 被静默登出
                SupabaseClientService.Client.Auth.AddStateChangedListener(OnAuthStateChanged);

                // 尝试从本地文件恢复 session
                var savedSession = LoadSessionFromFile();
                if (savedSession != null)
                {
                    try
                    {
                        // 使用保存的 session 恢复登录状态（token 为空时清除文件，避免异常）
                        if (string.IsNullOrEmpty(savedSession.AccessToken) || string.IsNullOrEmpty(savedSession.RefreshToken))
                        {
                            DeleteSessionFile();
                            return;
                        }
                        var session = await SupabaseClientService.Client.Auth.SetSession(
                            savedSession.AccessToken!,
                            savedSession.RefreshToken!);

                        if (session?.User != null)
                        {
                            CurrentUser = session.User;
                            LoginStateChanged?.Invoke(this, true);
                            return;
                        }
                    }
                    catch (Exception ex) when (IsTransientNetworkError(ex))
                    {
                        // M7 修复：网络未就绪等瞬态故障不删除凭据文件，
                        // 保留供下次启动重试（原实现一律删文件导致开机离线时被登出）
                        System.Diagnostics.Debug.WriteLine($"Restore session skipped (transient network error): {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Restore session failed: {ex.Message}");
                        DeleteSessionFile();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AuthService Initialize error: {ex.Message}");
            }
        }

        /// <summary>
        /// Gotrue 认证状态变化回调：token 自动刷新后将新凭据回写本地文件。
        /// </summary>
        private void OnAuthStateChanged(object sender, AuthState state)
        {
            if (state == AuthState.TokenRefreshed)
            {
                try
                {
                    var session = SupabaseClientService.Client.Auth.CurrentSession;
                    if (session != null)
                        SaveSessionToFile(session);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Save refreshed session failed: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// 邮箱密码登录
        /// </summary>
        public async Task<AuthResult> LoginWithEmailPasswordAsync(string email, string password)
        {
            try
            {
                var session = await SupabaseClientService.Client.Auth.SignIn(email, password);
                
                if (session?.User != null)
                {
                    CurrentUser = session.User;
                    LoginStateChanged?.Invoke(this, true);
                    
                    // 保存 session 到本地文件
                    SaveSessionToFile(session);
                    
                    return new AuthResult { Success = true };
                }
                
                return new AuthResult { Success = false, Error = "登录失败" };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Login error: {ex.Message}");
                return new AuthResult { Success = false, Error = ex.Message };
            }
        }
        
        /// <summary>
        /// 邮箱注册
        /// </summary>
        public async Task<AuthResult> SignUpWithEmailPasswordAsync(string email, string password)
        {
            try
            {
                var result = await SupabaseClientService.Client.Auth.SignUp(email, password);
                
                if (result?.User != null)
                {
                    // 注册成功，可能需要邮箱验证
                    return new AuthResult 
                    { 
                        Success = true, 
                        Message = "注册成功，请检查邮箱进行验证" 
                    };
                }
                
                return new AuthResult { Success = false, Error = "注册失败" };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SignUp error: {ex.Message}");
                return new AuthResult { Success = false, Error = ex.Message };
            }
        }
        
        /// <summary>
        /// 退出登录
        /// </summary>
        public async Task LogoutAsync()
        {
            try
            {
                await SupabaseClientService.Client.Auth.SignOut();
                CurrentUser = null;
                LoginStateChanged?.Invoke(this, false);
                
                // 删除本地 session 文件
                DeleteSessionFile();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Logout error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 重置密码
        /// </summary>
        public async Task<AuthResult> ResetPasswordAsync(string email)
        {
            try
            {
                await SupabaseClientService.Client.Auth.ResetPasswordForEmail(email);
                return new AuthResult { Success = true, Message = "重置密码邮件已发送" };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ResetPassword error: {ex.Message}");
                return new AuthResult { Success = false, Error = ex.Message };
            }
        }
        
        // ========== Session 持久化方法 ==========
        
        /// <summary>
        /// 保存 session 到本地文件（DPAPI 加密 + 原子写，加密失败不落盘）
        /// </summary>
        private void SaveSessionToFile(Session session)
        {
            // M9 修复：TokenRefreshed（Gotrue 后台线程）与登录保存（UI 线程）可能并发写文件，串行化
            lock (_sessionFileLock)
            {
                try
                {
                    var dir = Path.GetDirectoryName(SessionFilePath);
                    if (dir != null && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    var sessionData = new SessionData
                    {
                        AccessToken = session.AccessToken,
                        RefreshToken = session.RefreshToken,
                        ExpiresAt = session.ExpiresAt(),
                        UserId = session.User?.Id
                    };

                    var json = JsonSerializer.Serialize(sessionData, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                    // DPAPI 加密：失败会抛异常（DataProtectionHelper 不再回退明文），此处不落盘
                    var encrypted = DataProtectionHelper.Protect(json);

                    // 原子写：先写随机名临时文件再替换（M9 修复：固定 .tmp 名在并发时会互相踩踏抛 IOException）
                    var tempPath = SessionFilePath + $".{Guid.NewGuid():N}.tmp";
                    try
                    {
                        File.WriteAllText(tempPath, encrypted);
                        File.Move(tempPath, SessionFilePath, overwrite: true);
                        System.Diagnostics.Debug.WriteLine("Session saved to file");
                    }
                    finally
                    {
                        try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    // M9 修复：写盘瞬时失败只记录日志并保留旧 session 文件，
                    // 不再删除唯一凭据导致莫名登出（加密失败本就不会落盘，无需清理）
                    System.Diagnostics.Debug.WriteLine($"SaveSessionToFile error: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// 从本地文件加载 session
        /// </summary>
        private SessionData? LoadSessionFromFile()
        {
            try
            {
                if (!File.Exists(SessionFilePath))
                    return null;
                
                var raw = File.ReadAllText(SessionFilePath);
                // 仅接受 DPAPI 密文；解密失败/明文数据一律清除并重新登录，杜绝明文凭据通道
                var json = DataProtectionHelper.Unprotect(raw);
                if (json == null)
                {
                    System.Diagnostics.Debug.WriteLine("Session file is not valid DPAPI data, clearing");
                    DeleteSessionFile();
                    return null;
                }
                var sessionData = JsonSerializer.Deserialize<SessionData>(json);

                // M8 修复：移除 access token 过期预检——access token 默认 1 小时过期，
                // 但 refresh token 通常仍有效，SetSession 会自动刷新续期。
                // 原实现关闭超过 1 小时后再打开就直接删凭据强制重新登录，体验极差。

                return sessionData;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadSessionFromFile error: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 判断是否为瞬态网络类异常（M7 修复）。
        /// 此类错误下保留本地凭据文件，下次启动重试；仅凭据确认失效时才删除。
        /// </summary>
        private static bool IsTransientNetworkError(Exception ex)
        {
            switch (ex)
            {
                case System.Net.Http.HttpRequestException:
                case System.Net.Sockets.SocketException:
                case System.Net.WebException:
                case TaskCanceledException: // 超时
                    return true;
            }
            var msg = ex.Message ?? string.Empty;
            return msg.Contains("network", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("connection", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("name or service", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("temporary failure", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 删除本地 session 文件
        /// </summary>
        private void DeleteSessionFile()
        {
            try
            {
                if (File.Exists(SessionFilePath))
                {
                    File.Delete(SessionFilePath);
                    System.Diagnostics.Debug.WriteLine("Session file deleted");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DeleteSessionFile error: {ex.Message}");
            }
        }
    }
    
    /// <summary>
    /// 认证结果
    /// </summary>
    public class AuthResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? Error { get; set; }
    }
    
    /// <summary>
    /// Session 持久化数据
    /// </summary>
    public class SessionData
    {
        /// <summary>
        /// 访问令牌
        /// </summary>
        public string? AccessToken { get; set; }
        
        /// <summary>
        /// 刷新令牌
        /// </summary>
        public string? RefreshToken { get; set; }
        
        /// <summary>
        /// 过期时间
        /// </summary>
        public DateTime? ExpiresAt { get; set; }
        
        /// <summary>
        /// 用户 ID
        /// </summary>
        public string? UserId { get; set; }
    }
}
