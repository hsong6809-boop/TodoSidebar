using System;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text.Json;
using System.Threading;
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
        /// M37：认证请求超时秒数。Gotrue 库内部 HttpClient 无短超时，
        /// 网络不通（如 supabase.co 被墙/被拦截）时请求会挂起约 100 秒，
        /// 期间登录窗口全部按钮被禁用，用户感知为"点啥都没反应"。
        /// 这里统一用短超时快速失败并给出可行动的错误提示。
        /// </summary>
        private const int AuthTimeoutSeconds = 15;

        /// <summary>给认证请求套上短超时，并对"快速失败型"网络错误自动重试。
        /// M38：大陆到 supabase.co（Cloudflare 边缘）的干扰是间歇性的——
        /// 同一台机器同一网络，前一分钟 TLS 被重置、后一分钟完全正常。
        /// 因此对 SSL 重置/连接中断这类秒级失败的瞬时网络错误自动快速重试，
        /// 命中"好窗口"即可成功；整体超时（15 秒挂起）不自动重试，避免长时间无反馈。</summary>
        private static async Task<T> WithAuthTimeout<T>(Func<Task<T>> operation)
        {
            const int MaxAttempts = 3;
            for (int attempt = 1; ; attempt++)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(AuthTimeoutSeconds));
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    // M38b：整个调用放到线程池执行。gotrue 库内部存在同步等待异步的代码路径，
                    // 在 WPF UI 线程（带 SynchronizationContext）上调用会死锁——表现为点击登录后
                    // 永远停留在"登录中..."且任何超时机制都无法生效（await 根本没到达）。
                    // 线程池线程没有同步上下文，从根源规避；同时让下方超时/重试真正生效。
                    return await Task.Run(operation).WaitAsync(cts.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (attempt < MaxAttempts && IsFastTransientNetworkError(ex, sw.Elapsed))
                {
                    System.Diagnostics.Debug.WriteLine($"Auth transient error (attempt {attempt}/{MaxAttempts}), retrying: {ex.Message}");
                    await Task.Delay(500).ConfigureAwait(false);
                }
            }
        }

        /// <summary>无返回值版本，复用泛型实现的超时与重试逻辑。</summary>
        private static Task WithAuthTimeout(Func<Task> operation)
            => WithAuthTimeout<object?>(async () =>
            {
                await operation().ConfigureAwait(false);
                return null;
            });

        /// <summary>
        /// 是否为值得重试的"快速失败型"瞬时网络错误：
        /// - 仅当失败发生得很快（&lt;5 秒）才重试——慢失败说明链路整体不通，重试大概率无效；
        /// - 整体超时（TaskCanceledException）不重试；
        /// - 配置缺失（InvalidOperationException 等）不属于网络错误，直接抛出。
        /// </summary>
        private static bool IsFastTransientNetworkError(Exception ex, TimeSpan elapsed)
        {
            if (elapsed > TimeSpan.FromSeconds(5)) return false;
            switch (ex)
            {
                case TaskCanceledException:
                    return false;
                case HttpRequestException:
                case SocketException:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 把底层网络异常翻译成用户能行动的中文提示：
        /// - 超时：网络无法到达服务器（最常见：目标网络直连 supabase.co 不通，需代理/换网络）
        /// - SSL 内层异常：TLS 握手被重置（典型于网络拦截）
        /// - 其他 HttpRequestException/SocketException：断网/DNS 失败
        /// </summary>
        private static string FriendlyAuthError(Exception ex)
        {
            switch (ex)
            {
                case TaskCanceledException:
                case TimeoutException:
                    return $"连接服务器超时（{AuthTimeoutSeconds} 秒）。当前网络到同步服务器的链路可能暂时不稳定，请稍后重试几次；持续失败请检查网络或使用代理";
                case HttpRequestException hre when FindInner<AuthenticationException>(hre) != null:
                    return "与同步服务器的安全连接（SSL）被重置：当前网络链路到服务器暂时不稳定，程序已自动重试仍失败，请稍后再试几次或更换网络";
                case HttpRequestException:
                    return "无法连接同步服务器，请检查网络连接后重试";
                case SocketException:
                    return "网络不可用，请检查网络连接后重试";
                default:
                    return ex.Message;
            }
        }

        private static Exception? FindInner<T>(Exception ex) where T : Exception
        {
            var cur = ex;
            while (cur != null)
            {
                if (cur is T) return cur;
                cur = cur.InnerException;
            }
            return null;
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
                        var session = await WithAuthTimeout(() =>
                            SupabaseClientService.Client.Auth.SetSession(
                                savedSession.AccessToken!,
                                savedSession.RefreshToken!));

                        if (session?.User != null)
                        {
                            CurrentUser = session.User;
                            LogAuthDiag("[auth] 会话恢复成功，后台触发状态处理器");
                            FireLoginStateChanged(true);
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
        
        /// <summary>认证链路诊断日志（与登录窗口共用 login_diag.txt）。</summary>
        internal static void LogAuthDiag(string message)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "TodoSidebar", "logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(
                    Path.Combine(dir, "login_diag.txt"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch { /* 日志失败不影响主流程 */ }
        }

        /// <summary>
        /// M38c：后台触发登录状态变化事件。处理器中的数据库/同步初始化不再阻塞认证返回路径。
        /// </summary>
        private void FireLoginStateChanged(bool isLoggedIn)
        {
            var handlers = LoginStateChanged;
            if (handlers == null)
            {
                LogAuthDiag($"[auth] LoginStateChanged 无订阅者({isLoggedIn})");
                return;
            }

            _ = Task.Run(() =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    handlers.Invoke(this, isLoggedIn);
                    LogAuthDiag($"[auth] 全部状态处理器执行完毕, 耗时={sw.ElapsedMilliseconds}ms");
                }
                catch (Exception ex)
                {
                    LogAuthDiag($"[auth] 状态处理器异常({sw.ElapsedMilliseconds}ms): {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                }
            });
        }

        /// <summary>
        /// 邮箱密码登录
        /// </summary>
        public async Task<AuthResult> LoginWithEmailPasswordAsync(string email, string password)
        {
            LogAuthDiag("[auth] LoginWithEmailPasswordAsync 进入");
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var session = await WithAuthTimeout(() =>
                    SupabaseClientService.Client.Auth.SignIn(email, password));
                LogAuthDiag($"[auth] SignIn 返回: 耗时={sw.ElapsedMilliseconds}ms User={(session?.User != null ? session.User.Id : "null")}");

                if (session?.User != null)
                {
                    CurrentUser = session.User;
                    SaveSessionToFile(session);
                    LogAuthDiag($"[auth] session 已保存({(File.Exists(SessionFilePath) ? "文件存在" : "文件缺失!")})，即将触发 LoginStateChanged");

                    // M38c：异步触发状态变化处理器。原先同步 Invoke 会把处理器的同步前缀
                    // （EnsureUserScope 建库归属、SyncService 初始化）串在登录返回路径上，
                    // 任一环卡住 => 登录永远停在"登录中"。现在立即返回，处理器后台执行。
                    FireLoginStateChanged(true);

                    LogAuthDiag("[auth] LoginStateChanged 已后台触发，登录方法返回");
                    return new AuthResult { Success = true };
                }

                LogAuthDiag("[auth] SignIn 返回但 User 为空");
                return new AuthResult { Success = false, Error = "登录失败" };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Login error: {ex.Message}");
                LogAuthDiag($"[auth] SignIn 异常: {ex.GetType().Name}: {ex.Message}");
                return new AuthResult { Success = false, Error = FriendlyAuthError(ex) };
            }
        }
        
        /// <summary>
        /// 邮箱注册
        /// </summary>
        public async Task<AuthResult> SignUpWithEmailPasswordAsync(string email, string password)
        {
            try
            {
                var result = await WithAuthTimeout(() =>
                    SupabaseClientService.Client.Auth.SignUp(email, password));

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
                return new AuthResult { Success = false, Error = FriendlyAuthError(ex) };
            }
        }
        
        /// <summary>
        /// 退出登录
        /// </summary>
        public async Task LogoutAsync()
        {
            try
            {
                await WithAuthTimeout(() => SupabaseClientService.Client.Auth.SignOut());
                CurrentUser = null;
                FireLoginStateChanged(false);

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
                await WithAuthTimeout(() =>
                    SupabaseClientService.Client.Auth.ResetPasswordForEmail(email));
                return new AuthResult { Success = true, Message = "重置密码邮件已发送" };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ResetPassword error: {ex.Message}");
                return new AuthResult { Success = false, Error = FriendlyAuthError(ex) };
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
