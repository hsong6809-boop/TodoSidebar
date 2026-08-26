using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using TodoSidebar.Services;

namespace TodoSidebar
{
    /// <summary>
    /// v5.2 账号中心窗口：身份卡（大头像/UID/邮箱/成长摘要）、昵称编辑、
    /// 内置头像选择、自定义头像上传、设备与同步信息。
    /// </summary>
    public partial class AccountWindow : Window
    {
        private readonly AccountService _account = AccountService.Instance;

        /// <summary>首字符兜底来源（昵称优先，回退邮箱前缀）。</summary>
        public string AvatarFallback { get; private set; } = string.Empty;

        private bool _suppressSwatchRebuild;

        public AccountWindow()
        {
            InitializeComponent();

            _account.ProfileChanged += OnProfileChanged;
            Closed += (_, _) => _account.ProfileChanged -= OnProfileChanged;

            RefreshAll();
        }

        // ==================== 渲染刷新 ====================

        private void OnProfileChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(RefreshAll);
        }

        private void RefreshAll()
        {
            var email = AuthService.Instance.CurrentUser?.Email ?? string.Empty;
            var nick = _account.Nickname;

            AvatarFallback = nick.Length > 0 ? nick : email;
            DisplayNameText.Text = nick.Length > 0 ? nick : (email.Length > 0 ? email.Split('@')[0] : "未登录账号");
            UidText.Text = _account.IsProvisioned ? $"UID {_account.Uid}" : "UID 连接云端后分配";
            EmailText.Text = email;

            // 成长摘要（本地成长档案，随同步合并）
            try
            {
                var g = DatabaseService.Instance.GetUserGrowth();
                GrowthSummaryText.Text = $"Lv.{g.Level} · {g.Title} · 连击 {g.ComboDays} 天";
            }
            catch { GrowthSummaryText.Text = ""; }

            // 头像主视图
            RefreshAvatarSource(HeroAvatar, 88);

            // 昵称输入框（未聚焦时才跟随，避免打断编辑）
            if (!NicknameInput.IsFocused)
                NicknameInput.Text = nick;

            // 内置头像色板选中态
            BuildAvatarSwatches();

            // 设备与同步
            DeviceText.Text = $"本机：{Environment.MachineName}";
            try
            {
                var userId = AuthService.Instance.CurrentUser?.Id;
                var raw = userId != null
                    ? DatabaseService.Instance.GetSetting($"LastSyncTimeUtc:{userId}")
                    : null;
                SyncTimeText.Text = DateTime.TryParse(raw, null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var t)
                    ? $"上次云同步：{t.ToLocalTime():yyyy-MM-dd HH:mm}"
                    : "尚未进行过云同步";
            }
            catch { SyncTimeText.Text = ""; }
        }

        private void RefreshAvatarSource(Controls.AvatarView view, double decodePx)
        {
            Controls.AvatarLoader.Load(view, _account, decodePx, AvatarFallback);
        }

        // ==================== 内置头像色板 ====================

        private void BuildAvatarSwatches()
        {
            if (AvatarSwatchPanel == null) return;
            _suppressSwatchRebuild = true;
            AvatarSwatchPanel.Children.Clear();

            foreach (var item in Controls.AvatarCatalog.Items)
            {
                bool selected = string.Equals(_account.AvatarKind, item.Kind, StringComparison.OrdinalIgnoreCase);

                // v5.5 等级解锁判定（当前使用中的不回收，仅锁"更换"动作）
                bool unlocked = Services.UnlockService.IsAvatarUnlocked(item.Kind, _account.AvatarKind);
                int required = Services.UnlockService.AvatarRequiredLevel(item.Kind);

                var avatar = new Controls.AvatarView
                {
                    Width = 40,
                    Height = 40,
                    Kind = item.Kind,
                    Opacity = unlocked ? 1 : 0.4,
                    Margin = new Thickness(0, 0, 7, 7),
                    Cursor = Cursors.Hand,
                    ToolTip = unlocked ? $"{item.Name} 头像" : $"{item.Name} · Lv.{required} 解锁"
                };

                var swatch = new Border
                {
                    Child = avatar,
                    Padding = new Thickness(3),
                    CornerRadius = new CornerRadius(999),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(2),
                    BorderBrush = selected
                        ? new System.Windows.Media.SolidColorBrush(item.To)
                        : System.Windows.Media.Brushes.Transparent,
                    Tag = item.Kind
                };
                swatch.MouseLeftButtonDown += BuiltInAvatar_Click;
                AvatarSwatchPanel.Children.Add(swatch);
            }
            _suppressSwatchRebuild = false;
        }

        private async void BuiltInAvatar_Click(object sender, MouseButtonEventArgs e)
        {
            if (_suppressSwatchRebuild) return;
            if (sender is FrameworkElement { Tag: string kind })
            {
                if (!Services.UnlockService.IsAvatarUnlocked(kind, _account.AvatarKind))
                {
                    MessageBox.Show(this,
                        $"该头像需要等级达到 Lv.{Services.UnlockService.AvatarRequiredLevel(kind)} 后解锁。\n完成任务和番茄即可升级！",
                        "尚未解锁", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                await _account.SetBuiltInAvatarAsync(kind);
            }
        }

        // ==================== 自定义头像 ====================

        private async void UploadAvatar_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择头像图片",
                Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.webp|所有文件|*.*"
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                UploadAvatarButton.IsEnabled = false;
                await _account.SetCustomAvatarAsync(dialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "设置头像失败",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                UploadAvatarButton.IsEnabled = true;
            }
        }

        // ==================== 昵称 / UID ====================

        private async void NicknameSave_Click(object sender, RoutedEventArgs e)
            => await SaveNicknameAsync();

        private async void NicknameInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await SaveNicknameAsync();
            }
        }

        private async Task SaveNicknameAsync()
        {
            NicknameHint.Text = "";
            await _account.SetNicknameAsync(NicknameInput.Text);
            NicknameHint.Text = "已保存 ✓";
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.4) };
            timer.Tick += (s, args) => { timer.Stop(); NicknameHint.Text = ""; };
            timer.Start();
        }

        private void CopyUid_Click(object sender, RoutedEventArgs e)
        {
            if (!_account.IsProvisioned) return;
            try { Clipboard.SetText(_account.Uid); } catch { /* 剪贴板被占用时静默 */ }
            if (sender is Button btn)
            {
                btn.Content = "已复制";
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
                timer.Tick += (s, args) => { timer.Stop(); btn.Content = "复制"; };
                timer.Start();
            }
        }

        // ==================== 窗口 ====================

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try { DragMove(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Header drag error: {ex.Message}"); }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
