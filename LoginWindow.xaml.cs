using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TodoSidebar.Config;
using TodoSidebar.ViewModels;
using TodoSidebar.Services;

namespace TodoSidebar
{
    public partial class LoginWindow : Window
    {
        // 异步操作进行中标记，防止登录/注册/忘记密码并发提交
        private bool _isBusy;

        /// <summary>L22 修复：UI 展示的模糊配置提示（不含任何 AnonKey 片段）</summary>
        private const string ConfigHint = "\n\n[诊断] 配置可能有误，详见日志";

        public LoginWindow()
        {
            InitializeComponent();
            LoadSavedCredentials();

            // P2：真实亚克力背板（默认关闭；设置 AcrylicEnabled=true 可开启，失败静默降级）
            Loaded += (_, _) => DwmBackdropHelper.ApplyMainShellAcrylic(this);

            // M37：进入登录页即后台预检同步服务器连通性（不阻塞 UI），
            // 网络不通时提前给出可行动提示，而不是等用户点登录后"卡住无反应"
            _ = RunConnectivityPreflightAsync();
        }

        /// <summary>
        /// M37：预检 Supabase /auth/v1/health（5 秒超时）。
        /// - 配置缺失：直接显示明确配置错误；
        /// - 网络不通/SSL 被重置：显示"需要代理/换网络"提示；
        /// 成功则不打扰用户。
        /// </summary>
        private async Task RunConnectivityPreflightAsync()
        {
            string url;
            try
            {
                url = SupabaseConfig.Url;
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
                return;
            }

            try
            {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                using var resp = await http.GetAsync(url.TrimEnd('/') + "/auth/v1/health");
                if (!resp.IsSuccessStatusCode)
                {
                    ShowWarning($"同步服务器响应异常（HTTP {(int)resp.StatusCode}），登录可能失败");
                }
            }
            catch (Exception)
            {
                ShowWarning("⚠ 当前网络连接同步服务器不稳定：登录/注册可能失败。链路干扰是间歇性的，可稍等片刻多点几次重试；持续失败请检查网络或使用代理");
            }
        }

        /// <summary>与 ShowError 同区域展示警告类提示（带 ⚠ 前缀区分）。</summary>
        private void ShowWarning(string message)
        {
            ShowError(message);
        }

        /// <summary>密码可见性切换：同步明文框与密码框内容。</summary>
        private void RevealToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (PasswordBox == null || PasswordRevealBox == null) return;

            if (RevealToggle != null && RevealToggle.IsChecked == true)
            {
                PasswordRevealBox.Text = PasswordBox.Password;
                PasswordBox.Visibility = Visibility.Collapsed;
                PasswordRevealBox.Visibility = Visibility.Visible;
            }
            else
            {
                PasswordBox.Password = PasswordRevealBox.Text;
                PasswordRevealBox.Visibility = Visibility.Collapsed;
                PasswordBox.Visibility = Visibility.Visible;
            }
        }

        private void LoadSavedCredentials()
        {
            try
            {
                var db = DatabaseService.Instance;
                var savedEmail = db.GetSetting("SavedEmail");
                var encryptedPassword = db.GetSetting("SavedPassword");
                var rememberMe = db.GetSetting("RememberMe");

                if (rememberMe == "1" && !string.IsNullOrEmpty(savedEmail))
                {
                    EmailTextBox.Text = savedEmail;
                    if (!string.IsNullOrEmpty(encryptedPassword))
                    {
                        // 解密失败（换机器/旧明文数据）时置空，不展示错误凭据
                        PasswordBox.Password = DataProtectionHelper.Unprotect(encryptedPassword) ?? "";
                    }
                    RememberMeCheckBox.IsChecked = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadSavedCredentials error: {ex.Message}");
            }
        }

        private void SaveCredentials(string email, string password)
        {
            try
            {
                var db = DatabaseService.Instance;
                if (RememberMeCheckBox.IsChecked == true)
                {
                    db.SetSetting("SavedEmail", email);
                    db.SetSetting("SavedPassword", DataProtectionHelper.Protect(password));
                    db.SetSetting("RememberMe", "1");
                }
                else
                {
                    db.SetSetting("SavedEmail", "");
                    db.SetSetting("SavedPassword", "");
                    db.SetSetting("RememberMe", "0");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SaveCredentials error: {ex.Message}");
            }
        }
        
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                DragMove();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DragMove error: {ex.Message}");
            }
        }

        /// <summary>
        /// L22 修复：无边框窗口没有系统标题栏，补充关闭按钮（见 LoginWindow.xaml 标题栏 ✕）。
        /// </summary>
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
        
        /// <summary>
        /// 统一禁用/启用全部操作按钮（M36b）。
        /// 注册与忘记密码按钮在 XAML 中未命名，故遍历可视树按类型收集；
        /// 本窗口恰好只有登录/注册/忘记密码三个按钮，不会误伤其他控件。
        /// </summary>
        private void SetBusy(bool busy)
        {
            _isBusy = busy;
            SetButtonsEnabled(this, !busy);
        }

        private static void SetButtonsEnabled(DependencyObject parent, bool enabled)
        {
            var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is Button button && button.Name != "CloseButton")
                {
                    // M37 修复：busy 时保留右上角关闭按钮可用，
                    // 否则网络挂起时用户连窗口都关不掉
                    button.IsEnabled = enabled;
                }
                SetButtonsEnabled(child, enabled);
            }
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            await LoginAsync();
        }

        private async Task LoginAsync()
        {
            // 已有异步操作进行中，忽略重复提交
            if (_isBusy) return;

            var email = EmailTextBox.Text.Trim();
            var password = PasswordBox.Password;

            // 验证输入
            if (string.IsNullOrEmpty(email))
            {
                ShowError("请输入邮箱");
                return;
            }

            if (!IsValidEmail(email))
            {
                ShowError("请输入有效的邮箱地址");
                return;
            }

            if (string.IsNullOrEmpty(password))
            {
                ShowError("请输入密码");
                return;
            }

            // 禁用全部按钮，显示加载状态
            SetBusy(true);
            LoginButton.Content = "登录中...";
            HideError();

            // M37：请求发出即写日志（邮箱脱敏），远程排查时可区分
            // "请求已发出但无返回"（网络挂起）与"处理器根本没执行"
            LogLoginDiag($"[attempt] 登录请求已发出: {MaskEmail(email)}");

            try
            {
                var result = await AuthService.Instance.LoginWithEmailPasswordAsync(email, password);

                if (result.Success)
                {
                    LogLoginDiag("[ui] 认证成功，开始切换主窗口");

                    // 保存凭据（如果勾选了记住我）
                    SaveCredentials(email, password);
                    LogLoginDiag("[ui] 凭据保存完成");

                    // R22/R57 修复（审查 sync-M3/M1）：在创建主窗口前同步完成数据归属校验。
                    // ① 同步等待 EnsureUserScope（消除与后台处理器的竞态，新用户不再闪现上一账号数据）；
                    // ② 检测到"切换到不同账号 且 本地有未上云的脏数据"时，先弹窗确认——
                    //    原实现会无预警物理清库，离线积累的任务/回收站墓碑永久丢失。
                    var currentUserId = Services.AuthService.Instance.CurrentUser?.Id;
                    if (!string.IsNullOrEmpty(currentUserId))
                    {
                        var db = Services.DatabaseService.Instance;
                        var lastUserId = db.GetSetting("LastUserId");
                        var switchingAccount = !string.IsNullOrEmpty(lastUserId) && lastUserId != currentUserId;

                        if (switchingAccount)
                        {
                            var dirtyCount = db.GetDirtyTaskCount();
                            if (dirtyCount > 0 && !ConfirmDiscardDirtyData(dirtyCount))
                            {
                                // 用户取消切换：登出刚认证的新账号，留在登录窗口。
                                // 提示其可登回原账号完成同步后再切换
                                await Services.AuthService.Instance.LogoutAsync();
                                ShowError("已取消切换账号。本机仍有未同步的数据，建议先登录原账号完成同步。");
                                return;
                            }
                        }

                        db.EnsureUserScope(currentUserId);
                        LogLoginDiag("[ui] EnsureUserScope 同步完成");
                    }

                    // 登录成功：先释放旧 ViewModel（避免定时器/订阅泄漏），再初始化新实例
                    App.SharedViewModel?.Dispose();
                    LogLoginDiag($"[ui] 旧 ViewModel 处理完成(原为{(App.SharedViewModel == null ? "null" : "非null")})，开始创建 MainViewModel");
                    App.SharedViewModel = new ViewModels.MainViewModel();
                    LogLoginDiag("[ui] MainViewModel 创建完成，启动通知服务");
                    Services.NotificationService.Instance.Start();
                    LogLoginDiag("[ui] 通知服务已启动，创建 MainWindow");
                    var mainWindow = new MainWindow();
                    LogLoginDiag("[ui] MainWindow 构造完成，调用 Show()");
                    mainWindow.Show();
                    // R41 修复（审查 H4）：重登后把全局热键重新注册到新主窗口——
                    // 原实现热键只在应用启动时注册一次，登出销毁窗口后热键永久失效直到重启
                    App.AttachHotkeysTo(mainWindow);
                    LogLoginDiag("[ui] MainWindow.Show() 完成，热键已重新注册，关闭登录窗口");
                    Close();
                    return;
                }
                else
                {
                    // L22 修复：完整诊断（含 AnonKey 前 24 字符片段）只写入日志文件，
                    // UI 仅显示模糊提示，避免把 Key 片段暴露在界面上
                    var errMsg = (result.Error ?? "登录失败") + BuildConfigDiag();
                    LogLoginDiag("[ui] 认证失败: " + errMsg);
                    ShowError((result.Error ?? "登录失败") + ConfigHint);
                }
            }
            catch (Exception ex)
            {
                // L22 修复：同上，诊断细节仅入日志
                var errMsg = $"登录出错: {ex.Message}" + BuildConfigDiag();
                LogLoginDiag("[ui] 切换过程异常: " + ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace);
                ShowError($"登录出错: {ex.Message}" + ConfigHint);
            }
            finally
            {
                SetBusy(false);
                LoginButton.Content = "登录";
            }
        }

        /// <summary>
        /// R57 修复（审查 M1）：切换账号且本地存在未上云数据时的确认弹窗。
        /// 返回 true 表示用户接受丢失并继续切换。
        /// </summary>
        private bool ConfirmDiscardDirtyData(int dirtyCount)
        {
            var result = MessageBox.Show(
                $"本机有 {dirtyCount} 条尚未同步到云端的修改（包括新建、编辑的任务，以及回收站中尚未上传的删除记录）。\n\n" +
                "切换到其他账号后，这些数据将从本机清除且无法恢复。\n\n" +
                "建议先取消，登录原账号完成同步后再切换。\n\n" +
                "确定要继续切换吗？",
                "发现未同步的数据", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            return result == MessageBoxResult.Yes;
        }

        /// <summary>
        /// 登录失败时把完整错误 + 配置诊断写入日志文件，便于排查（不回显完整 Key）。
        /// </summary>
        private static void LogLoginDiag(string message)        {
            try
            {
                var dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "TodoSidebar", "logs");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(dir, "login_diag.txt"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LogLoginDiag error: {ex.Message}");
            }
        }
        
        private async void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            // 已有异步操作进行中，忽略重复提交
            if (_isBusy) return;

            var email = EmailTextBox.Text.Trim();
            var password = PasswordBox.Password;

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                ShowError("请输入邮箱和密码进行注册");
                return;
            }

            SetBusy(true);
            LoginButton.Content = "注册中...";
            HideError();
            LogLoginDiag($"[attempt] 注册请求已发出: {MaskEmail(email)}");

            try
            {
                var result = await AuthService.Instance.SignUpWithEmailPasswordAsync(email, password);

                if (result.Success)
                {
                    MessageBox.Show(result.Message ?? "注册成功", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    ShowError(result.Error ?? "注册失败");
                }
            }
            catch (Exception ex)
            {
                ShowError($"注册出错: {ex.Message}");
            }
            finally
            {
                SetBusy(false);
                LoginButton.Content = "登录";
            }
        }

        private async void ForgotPassword_Click(object sender, RoutedEventArgs e)
        {
            // 已有异步操作进行中，忽略重复提交
            if (_isBusy) return;

            var email = EmailTextBox.Text.Trim();

            if (string.IsNullOrEmpty(email))
            {
                ShowError("请输入邮箱后点击忘记密码");
                return;
            }

            SetBusy(true);
            LogLoginDiag($"[attempt] 忘记密码请求已发出: {MaskEmail(email)}");

            try
            {
                var result = await AuthService.Instance.ResetPasswordAsync(email);

                if (result.Success)
                {
                    MessageBox.Show(result.Message ?? "重置密码邮件已发送", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    ShowError(result.Error ?? "发送失败");
                }
            }
            catch (Exception ex)
            {
                ShowError($"发送重置邮件出错: {ex.Message}");
            }
            finally
            {
                SetBusy(false);
            }
        }
        
        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// 登录失败时生成配置诊断（URL 与 Key 前缀），用于排查 Invalid API key。
        /// L22 修复：本方法结果仅允许写入 login_diag.txt 日志，禁止直接展示到 UI；
        /// 仅显示 Key 指纹（SHA-256 前 8 位十六进制），不回显任何 Key 片段。
        /// R21 修复（审查 sync-L2）：原实现把 AnonKey 前 24 字符写入明文诊断日志，
        /// 属于不必要的凭据材料扩散；指纹足以核对配置是否一致。
        /// </summary>
        private static string BuildConfigDiag()
        {
            string url = "?", key = "?";
            try { url = SupabaseConfig.Url; } catch (Exception ex) { url = "(读取异常: " + ex.Message + ")"; }
            try
            {
                var k = SupabaseConfig.AnonKey;
                key = string.IsNullOrEmpty(k)
                    ? "(空)"
                    : $"指纹={Fingerprint(k)} 长度={k.Length}";
            }
            catch (Exception ex)
            {
                key = "(读取异常: " + ex.Message + ")";
            }
            return $"\n\n[诊断] URL = {url}\nKey = {key}";
        }

        /// <summary>R21：凭据指纹——SHA-256 前 8 个十六进制字符，可用于核对但不泄露原文。</summary>
        private static string Fingerprint(string secret)
        {
            try
            {
                using var sha = System.Security.Cryptography.SHA256.Create();
                var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(secret));
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < 4; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
            catch
            {
                return "(计算失败)";
            }
        }
        
        private void HideError()
        {
            ErrorText.Visibility = Visibility.Collapsed;
        }

        private static bool IsValidEmail(string email)
        {
            try
            {
                return Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.None, TimeSpan.FromMilliseconds(200));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>M37：日志用邮箱脱敏（只保留域名部分），避免明文邮箱落盘。</summary>
        private static string MaskEmail(string email)
        {
            var at = email.IndexOf('@');
            return at > 0 ? "***" + email.Substring(at) : "***";
        }
    }
}
