using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
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
                var savedPassword = db.GetSetting("SavedPassword");
                var rememberMe = db.GetSetting("RememberMe");

                if (rememberMe == "1" && !string.IsNullOrEmpty(savedEmail))
                {
                    EmailTextBox.Text = savedEmail;
                    if (!string.IsNullOrEmpty(savedPassword))
                        PasswordBox.Password = savedPassword;
                    RememberMeCheckBox.IsChecked = true;
                }
            }
            catch { }
        }

        private void SaveCredentials(string email, string password)
        {
            try
            {
                var db = DatabaseService.Instance;
                if (RememberMeCheckBox.IsChecked == true)
                {
                    db.SetSetting("SavedEmail", email);
                    db.SetSetting("SavedPassword", password);
                    db.SetSetting("RememberMe", "1");
                }
                else
                {
                    db.SetSetting("SavedEmail", "");
                    db.SetSetting("SavedPassword", "");
                    db.SetSetting("RememberMe", "0");
                }
            }
            catch { }
        }
        
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                DragMove();
            }
            catch { }
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
                    
                    // 登录成功，初始化 ViewModel 并打开主窗口
                    App.SharedViewModel = new ViewModels.MainViewModel();
                    Services.NotificationService.Instance.Start();
                    var mainWindow = new MainWindow();
                    mainWindow.Show();
                    Close();
                }
                else
                {
                    ShowError(result.Error ?? "登录失败");
                }
            }
            catch (Exception ex)
            {
                ShowError($"登录出错: {ex.Message}");
            }
            finally
            {
                LoginButton.IsEnabled = true;
                LoginButton.Content = "登录";
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
        
        private void HideError()
        {
            ErrorText.Visibility = Visibility.Collapsed;
        }
    }
}
