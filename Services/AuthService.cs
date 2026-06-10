using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Supabase.Gotrue;
using Supabase.Gotrue.Interfaces;
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
                
                // 尝试从本地文件恢复 session
                var savedSession = LoadSessionFromFile();
                if (savedSession != null)
                {
                    try
                    {
                        // 使用保存的 session 恢复登录状态
                        var session = await SupabaseClientService.Client.Auth.SetSession(
                            savedSession.AccessToken, 
                            savedSession.RefreshToken);
                        
                        if (session?.User != null)
                        {
                            CurrentUser = session.User;
                            LoginStateChanged?.Invoke(this, true);
                            return;
                        }
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
        /// 保存 session 到本地文件
        /// </summary>
        private void SaveSessionToFile(Session session)
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
                // DPAPI 加密后再写入文件
                var encrypted = DataProtectionHelper.Protect(json);
                File.WriteAllText(SessionFilePath, encrypted);
                
                System.Diagnostics.Debug.WriteLine("Session saved to file");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SaveSessionToFile error: {ex.Message}");
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
                // DPAPI 解密（兼容旧版明文格式）
                var json = DataProtectionHelper.IsProtected(raw) 
                    ? DataProtectionHelper.Unprotect(raw) 
                    : raw;
                var sessionData = JsonSerializer.Deserialize<SessionData>(json);
                
                // 检查 session 是否过期
                if (sessionData?.ExpiresAt != null && sessionData.ExpiresAt < DateTime.UtcNow)
                {
                    System.Diagnostics.Debug.WriteLine("Session expired, deleting file");
                    DeleteSessionFile();
                    return null;
                }
                
                return sessionData;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadSessionFromFile error: {ex.Message}");
                return null;
            }
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
