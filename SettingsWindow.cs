using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TodoSidebar.Services;

namespace TodoSidebar
{
    public class SettingsWindow : Window
    {
        private readonly ThemeManager _themeManager;
        private readonly ExportService _exportService;

        public SettingsWindow()
        {
            _themeManager = ThemeManager.Instance;
            _exportService = new ExportService(DatabaseService.Instance);
            
            InitializeUI();
        }

        private void InitializeUI()
        {
            Title = "设置";
            Width = 450;
            Height = 550;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            
            try
            {
                Background = (Brush)FindResource("GlassBrush");
            }
            catch
            {
                Background = new SolidColorBrush(Color.FromRgb(240, 240, 245));
            }

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 标题栏
            var header = new Border
            {
                Padding = new Thickness(20, 15, 20, 15),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            
            try
            {
                header.Background = (Brush)FindResource("GlassLightBrush");
                header.BorderBrush = (Brush)FindResource("BorderBrush");
            }
            catch
            {
                header.Background = new SolidColorBrush(Color.FromRgb(245, 245, 250));
                header.BorderBrush = new SolidColorBrush(Color.FromArgb(50, 0, 0, 0));
            }

            header.Child = new TextBlock
            {
                Text = "⚙️ 设置",
                FontSize = 18,
                FontWeight = FontWeights.Bold
            };
            Grid.SetRow(header, 0);

            // 内容区域
            var content = new ScrollViewer
            {
                Padding = new Thickness(15)
            };

            var stack = new StackPanel();

            // 主题设置
            stack.Children.Add(CreateSectionHeader("🎨 主题设置"));
            
            var themePanel = new StackPanel { Margin = new Thickness(0, 5, 0, 15) };
            
            var lightRadio = new RadioButton { Content = "☀️ 浅色模式", Tag = "Light", Margin = new Thickness(0, 0, 0, 8) };
            lightRadio.Checked += Theme_Changed;

            var darkRadio = new RadioButton { Content = "🌙 深色模式", Tag = "Dark", Margin = new Thickness(0, 0, 0, 8) };
            darkRadio.Checked += Theme_Changed;

            var systemRadio = new RadioButton { Content = "💻 跟随系统", Tag = "System" };
            systemRadio.Checked += Theme_Changed;

            // 设置当前选中
            switch (_themeManager.CurrentTheme)
            {
                case ThemeType.Light: lightRadio.IsChecked = true; break;
                case ThemeType.Dark: darkRadio.IsChecked = true; break;
                case ThemeType.System: systemRadio.IsChecked = true; break;
            }

            themePanel.Children.Add(lightRadio);
            themePanel.Children.Add(darkRadio);
            themePanel.Children.Add(systemRadio);
            stack.Children.Add(themePanel);

            // 数据管理
            stack.Children.Add(CreateSectionHeader("💾 数据管理"));
            
            var dataPanel = new StackPanel { Margin = new Thickness(0, 5, 0, 15) };
            
            dataPanel.Children.Add(CreateButton("📤 导出为 JSON", ExportJson_Click));
            dataPanel.Children.Add(CreateButton("📤 导出为 CSV", ExportCsv_Click));
            dataPanel.Children.Add(CreateButton("📥 导入数据", Import_Click, isPrimary: false));
            dataPanel.Children.Add(CreateButton("💾 创建备份", Backup_Click));
            
            stack.Children.Add(dataPanel);

            // 关于
            stack.Children.Add(CreateSectionHeader("ℹ️ 关于"));
            
            var aboutPanel = new StackPanel { Margin = new Thickness(0, 5, 0, 15) };
            aboutPanel.Children.Add(new TextBlock { Text = "版本: 2.0.0 (优化版)", Margin = new Thickness(0, 0, 0, 5) });
            aboutPanel.Children.Add(new TextBlock { Text = "一个优雅的待办事项侧边栏应用" });
            stack.Children.Add(aboutPanel);

            content.Content = stack;
            Grid.SetRow(content, 1);

            // 底部按钮
            var footer = new Border
            {
                Padding = new Thickness(15),
                BorderThickness = new Thickness(0, 1, 0, 0)
            };
            
            try
            {
                footer.Background = (Brush)FindResource("GlassLightBrush");
                footer.BorderBrush = (Brush)FindResource("BorderBrush");
            }
            catch
            {
                footer.Background = new SolidColorBrush(Color.FromRgb(245, 245, 250));
                footer.BorderBrush = new SolidColorBrush(Color.FromArgb(50, 0, 0, 0));
            }

            var closeBtn = new Button
            {
                Content = "关闭",
                Padding = new Thickness(25, 8, 25, 8),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            closeBtn.Click += (s, e) => Close();
            footer.Child = closeBtn;
            Grid.SetRow(footer, 2);

            mainGrid.Children.Add(header);
            mainGrid.Children.Add(content);
            mainGrid.Children.Add(footer);

            Content = mainGrid;
        }

        private TextBlock CreateSectionHeader(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            };
        }

        private Button CreateButton(string content, RoutedEventHandler clickHandler, bool isPrimary = true)
        {
            var btn = new Button
            {
                Content = content,
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(15, 10, 15, 10),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            
            btn.Click += clickHandler;
            return btn;
        }

        private void Theme_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton radio && radio.Tag is string themeStr)
            {
                if (Enum.TryParse<ThemeType>(themeStr, out var theme))
                {
                    _themeManager.CurrentTheme = theme;
                }
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
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "JSON 文件|*.json"
            };

            if (dialog.ShowDialog() == true)
            {
                var result = MessageBox.Show("导入将添加到现有数据，是否继续？", "确认",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        var count = _exportService.ImportFromJson(dialog.FileName);
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
