using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TodoSidebar.Config;
using TodoSidebar.ViewModels;
using TodoSidebar.Services;

namespace TodoSidebar
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
            LoadSavedCredentials();
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
        
        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            await LoginAsync();
        }
        
        private async Task LoginAsync()
        {
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
            
            // 禁用按钮，显示加载状态
            LoginButton.IsEnabled = false;
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
                    var errMsg = (result.Error ?? "登录失败") + BuildConfigDiag();
                    LogLoginDiag(errMsg);
                    ShowError(errMsg);
                }
            }
            catch (Exception ex)
            {
                var errMsg = $"登录出错: {ex.Message}" + BuildConfigDiag();
                LogLoginDiag(errMsg);
                ShowError(errMsg);
            }
            finally
            {
                LoginButton.IsEnabled = true;
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
            var email = EmailTextBox.Text.Trim();
            var password = PasswordBox.Password;
            
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                ShowError("请输入邮箱和密码进行注册");
                return;
            }
            
            LoginButton.IsEnabled = false;
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
                LoginButton.IsEnabled = true;
                LoginButton.Content = "登录";
            }
        }
        
        private async void ForgotPassword_Click(object sender, RoutedEventArgs e)
        {
            var email = EmailTextBox.Text.Trim();
            
            if (string.IsNullOrEmpty(email))
            {
                ShowError("请输入邮箱后点击忘记密码");
                return;
            }
            
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
        }
        
        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// 登录失败时附加配置诊断（URL 与 Key 前缀），用于排查 Invalid API key。
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
