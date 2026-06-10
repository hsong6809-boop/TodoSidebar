using System.Windows;
using System.Windows.Input;
using TodoSidebar.Services;

namespace TodoSidebar
{
    public partial class UpgradeDialog : Window
    {
        private readonly ILicenseService _licenseService;

        public UpgradeDialog() : this(null) { }

        public UpgradeDialog(ILicenseService? licenseService)
        {
            InitializeComponent();
            _licenseService = licenseService ?? App.Services.GetService(typeof(ILicenseService)) as ILicenseService
                              ?? new LicenseService();
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            if (_licenseService.IsPro)
            {
                CurrentTierText.Text = "Pro 版 ✅";
                TrialButton.IsEnabled = false;
                TrialButton.Content = "已是 Pro 用户";
                PurchaseButton.IsEnabled = false;
                PurchaseButton.Content = "已激活";
                StatusText.Text = "您已是 Pro 用户，所有功能已解锁。";
            }
            else if (_licenseService.IsTrialActive)
            {
                CurrentTierText.Text = "试用期";
                TrialStatusText.Text = $"剩余 {_licenseService.TrialDaysRemaining} 天";
                TrialButton.IsEnabled = false;
                TrialButton.Content = $"试用中（剩余 {_licenseService.TrialDaysRemaining} 天）";
                StatusText.Text = "试用期结束后可购买 Pro 继续使用。";
            }
            else
            {
                CurrentTierText.Text = "Free 版";
                TrialStatusText.Text = "部分功能受限";
                TrialButton.IsEnabled = true;
                TrialButton.Content = "🎁 开始 14 天免费试用";
                StatusText.Text = "";
            }
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void TrialButton_Click(object sender, RoutedEventArgs e)
        {
            _licenseService.StartTrial();
            UpdateStatus();
            StatusText.Text = "试用期已开始！14 天内可使用所有 Pro 功能。";
        }

        private void PurchaseButton_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "支付功能即将开放，敬请期待！";
        }

        private void ActivateButton_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "License Key 激活功能即将开放，敬请期待！";
        }
    }
}
