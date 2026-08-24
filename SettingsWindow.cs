using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TodoSidebar.Services;

namespace TodoSidebar
{
    /// <summary>
    /// 设置窗口（P2 已迁移为 XAML + 共享样式，逻辑与旧版一致）。
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly ThemeManager _themeManager;
        private readonly ExportService _exportService;

        public SettingsWindow()
        {
            InitializeComponent();

            _themeManager = ThemeManager.Instance;
            _exportService = new ExportService(DatabaseService.Instance);

            // 账户信息
            var licenseService = App.Services?.GetService(typeof(ILicenseService)) as ILicenseService;
            var tierText = licenseService?.IsPro == true ? "Pro 版 ✅" : "Free 版";
            AccountText.Text = $"当前版本：{tierText}";

            // 版本号
            VersionText.Text = $"版本 {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "4.2.2"} · 每日任务";

            // 主题选中态
            switch (_themeManager.CurrentTheme)
            {
                case ThemeType.Light: ThemeLightRadio.IsChecked = true; break;
                case ThemeType.Dark: ThemeDarkRadio.IsChecked = true; break;
                case ThemeType.System: ThemeSystemRadio.IsChecked = true; break;
            }

            // V2-W2：强调色色板
            BuildAccentSwatches();

            // V2 收尾：减少动效开关初始态
            try { ReduceMotionCheck.IsChecked = DatabaseService.Instance.GetSetting("ReduceMotion") == "true"; }
            catch { ReduceMotionCheck.IsChecked = false; }

            // V2：昵称
            NicknameInput.Text = App.Nickname;
        }

        /// <summary>保存昵称：持久化 + 更新运行时缓存（侧边栏问候语/头像即时可用）。</summary>
        private void NicknameSave_Click(object sender, RoutedEventArgs e)
        {
            var nick = NicknameInput.Text.Trim();
            try { DatabaseService.Instance.SetSetting("Nickname", nick); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"保存昵称失败: {ex.Message}"); }
            App.Nickname = nick;

            // 轻量确认：按钮短暂变为"已保存"
            if (NicknameSaveButton != null)
            {
                NicknameSaveButton.Content = "已保存 ✓";
                var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
                t.Tick += (s, args) => { t.Stop(); NicknameSaveButton.Content = "保存"; };
                t.Start();
            }
        }

        /// <summary>动效开关切换：立即生效并持久化。</summary>
        private void ReduceMotion_Changed(object sender, RoutedEventArgs e)
        {
            if (ReduceMotionCheck == null) return;
            AnimationService.ReduceMotion = ReduceMotionCheck.IsChecked == true;
            try { DatabaseService.Instance.SetSetting("ReduceMotion", AnimationService.ReduceMotion ? "true" : "false"); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"保存 ReduceMotion 失败: {ex.Message}"); }
        }

        /// <summary>构建强调色色板（选中项带主色描边环）。</summary>
        private void BuildAccentSwatches()
        {
            if (AccentSwatchPanel == null) return;
            AccentSwatchPanel.Children.Clear();

            foreach (var palette in Services.ThemeManager.AccentPalettes)
            {
                var color = Services.ThemeManager.GetAccentBase(palette.Name);
                bool selected = string.Equals(_themeManager.CurrentAccent, palette.Name, StringComparison.OrdinalIgnoreCase);

                var dot = new System.Windows.Shapes.Ellipse
                {
                    Width = 20,
                    Height = 20,
                    Fill = new System.Windows.Media.SolidColorBrush(color)
                };

                var swatch = new Border
                {
                    Width = 30,
                    Height = 30,
                    CornerRadius = new CornerRadius(999),
                    Padding = new Thickness(3),
                    Background = System.Windows.Media.Brushes.Transparent,
                    BorderThickness = new Thickness(2),
                    BorderBrush = selected ? new System.Windows.Media.SolidColorBrush(color) : System.Windows.Media.Brushes.Transparent,
                    Child = dot,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Margin = new Thickness(0, 0, 8, 4),
                    ToolTip = $"{palette.Name} 强调色"
                };
                var name = palette.Name;
                swatch.MouseLeftButtonDown += (s, e) =>
                {
                    _themeManager.CurrentAccent = name;
                    BuildAccentSwatches();
                };

                AccentSwatchPanel.Children.Add(swatch);
            }
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try { DragMove(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Header drag error: {ex.Message}"); }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        /// <summary>
        /// M40：手动检查更新。不受每日门控限制；
        /// 发现新版本时弹确认框，确认后跳转浏览器打开 Release 下载页。
        /// </summary>
        private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            UpdateCheckButton.IsEnabled = false;
            UpdateStatusText.Text = "正在检查更新…";
            UpdateStatusText.Foreground = FindResource("TextSecondaryBrush") as System.Windows.Media.Brush
                ?? System.Windows.Media.Brushes.Gray;

            var info = await Services.UpdateChecker.CheckAsync();

            UpdateCheckButton.IsEnabled = true;

            if (info == null)
            {
                UpdateStatusText.Text = "检查失败，请稍后重试";
                return;
            }

            if (!info.HasUpdate)
            {
                UpdateStatusText.Text = $"当前已是最新版本（{info.CurrentVersion}）✓";
                return;
            }

            UpdateStatusText.Text = $"发现新版本 {info.RemoteVersion}！";
            UpdateStatusText.Foreground = FindResource("AccentBrush") as System.Windows.Media.Brush
                ?? System.Windows.Media.Brushes.DodgerBlue;

            Services.UpdateChecker.PromptDownload(info);
        }

        private void Theme_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.RadioButton radio && radio.Tag is string themeStr)
            {
                if (Enum.TryParse<ThemeType>(themeStr, out var theme))
                    _themeManager.CurrentTheme = theme;
            }
        }

        private void ExportJson_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON 文件|*.json",
                FileName = $"todo_backup_{DateTime.Now:yyyyMMdd}.json"
            };
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    _exportService.ExportToJson(dialog.FileName);
                    MessageBox.Show("导出成功！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV 文件|*.csv",
                FileName = $"todo_export_{DateTime.Now:yyyyMMdd}.csv"
            };
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    _exportService.ExportToCsv(dialog.FileName);
                    MessageBox.Show("导出成功！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "JSON 文件|*.json" };
            if (dialog.ShowDialog() == true)
            {
                var result = MessageBox.Show("导入将添加到现有数据，是否继续？", "确认",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        var count = _exportService.ImportFromJson(dialog.FileName);

                        // 导入成功后刷新主界面数据，使新任务立即可见
                        var vm = App.SharedViewModel;
                        if (vm != null) { vm.LoadData(); }

                        MessageBox.Show($"成功导入 {count} 条任务！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"导入失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void Backup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var backupPath = _exportService.CreateBackup();
                MessageBox.Show($"备份已创建：\n{backupPath}", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"备份失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
