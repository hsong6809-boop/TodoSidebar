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
                if (child is Button button)
                {
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

            try
            {
                var result = await AuthService.Instance.LoginWithEmailPasswordAsync(email, password);

                if (result.Success)
                {
                    // 保存凭据（如果勾选了记住我）
                    SaveCredentials(email, password);

                    // 登录成功：先释放旧 ViewModel（避免定时器/订阅泄漏），再初始化新实例
                    App.SharedViewModel?.Dispose();
                    App.SharedViewModel = new ViewModels.MainViewModel();
                    Services.NotificationService.Instance.Start();
                    var mainWindow = new MainWindow();
                    mainWindow.Show();
                    Close();
                }
                else
                {
                    // L22 修复：完整诊断（含 AnonKey 前 24 字符片段）只写入日志文件，
                    // UI 仅显示模糊提示，避免把 Key 片段暴露在界面上
                    var errMsg = (result.Error ?? "登录失败") + BuildConfigDiag();
                    LogLoginDiag(errMsg);
                    ShowError((result.Error ?? "登录失败") + ConfigHint);
                }
            }
            catch (Exception ex)
            {
                // L22 修复：同上，诊断细节仅入日志
                var errMsg = $"登录出错: {ex.Message}" + BuildConfigDiag();
                LogLoginDiag(errMsg);
                ShowError($"登录出错: {ex.Message}" + ConfigHint);
            }
            finally
            {
                SetBusy(false);
                LoginButton.Content = "登录";
            }
        }

        /// <summary>
        /// 登录失败时把完整错误 + 配置诊断写入日志文件，便于排查（不回显完整 Key）。
        /// </summary>
        private static void LogLoginDiag(string message)
        {
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
        /// 仅显示 Key 前 24 字符，不回显完整凭据。
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
                    : k.Substring(0, Math.Min(24, k.Length)) + $"... 长度={k.Length}";
            }
            catch (Exception ex)
            {
                key = "(读取异常: " + ex.Message + ")";
            }
            return $"\n\n[诊断] URL = {url}\nKey = {key}";
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
    }
}
