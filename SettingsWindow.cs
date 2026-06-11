using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
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

        private Brush GetBrush(string key, Color fallback)
        {
            try { return (Brush)FindResource(key); }
            catch { return new SolidColorBrush(fallback); }
        }

        private void InitializeUI()
        {
            Title = "设置";
            Width = 420;
            Height = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;

            // 主容器 - 圆角毛玻璃
            var mainBorder = new Border
            {
                Background = GetBrush("GlassBrush", Color.FromRgb(248, 249, 254)),
                CornerRadius = new CornerRadius(14),
                BorderBrush = GetBrush("BorderBrush", Color.FromArgb(26, 0, 0, 0)),
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 2, Opacity = 0.2 }
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ===== 标题栏 =====
            var header = new Border
            {
                Background = GetBrush("GlassLightBrush", Color.FromRgb(240, 242, 248)),
                Padding = new Thickness(20, 14, 20, 14),
                CornerRadius = new CornerRadius(14, 14, 0, 0),
                BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = GetBrush("BorderBrush", Color.FromArgb(26, 0, 0, 0)),
                Cursor = System.Windows.Input.Cursors.SizeAll
            };
            header.MouseLeftButtonDown += (s, e) => DragMove();

            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition());
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            headerGrid.Children.Add(new TextBlock
            {
                Text = "⚙️ 设置",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = GetBrush("TextBrush", Color.FromRgb(30, 41, 59)),
                VerticalAlignment = VerticalAlignment.Center
            });

            var closeBtn = new Button
            {
                Content = "✕",
                Width = 28,
                Height = 28,
                FontSize = 14,
                Background = Brushes.Transparent,
                Foreground = GetBrush("TextSecondaryBrush", Color.FromRgb(100, 116, 139)),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            closeBtn.Click += (s, e) => Close();
            Grid.SetColumn(closeBtn, 1);
            headerGrid.Children.Add(closeBtn);

            header.Child = headerGrid;
            Grid.SetRow(header, 0);

            // ===== 内容区域 =====
            var scrollViewer = new ScrollViewer
            {
                Padding = new Thickness(20, 16, 20, 16),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var stack = new StackPanel();

            // 账户信息
            stack.Children.Add(CreateCard("👤 账户", card =>
            {
                var licenseService = App.Services?.GetService(typeof(ILicenseService)) as ILicenseService;
                var tierText = licenseService?.IsPro == true ? "Pro 版 ✅" : "Free 版";
                card.Children.Add(new TextBlock
                {
                    Text = $"当前版本：{tierText}",
                    FontSize = 13,
                    Foreground = GetBrush("TextBrush", Color.FromRgb(30, 41, 59))
                });
            }));

            // 主题设置
            stack.Children.Add(CreateCard("🎨 主题", card =>
            {
                var lightRadio = CreateThemeRadio("☀️ 浅色", "Light");
                var darkRadio = CreateThemeRadio("🌙 深色", "Dark");
                var systemRadio = CreateThemeRadio("💻 跟随系统", "System");

                switch (_themeManager.CurrentTheme)
                {
                    case ThemeType.Light: lightRadio.IsChecked = true; break;
                    case ThemeType.Dark: darkRadio.IsChecked = true; break;
                    case ThemeType.System: systemRadio.IsChecked = true; break;
                }

                var radioStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
                radioStack.Children.Add(lightRadio);
                radioStack.Children.Add(darkRadio);
                radioStack.Children.Add(systemRadio);
                card.Children.Add(radioStack);
            }));

            // 数据管理
            stack.Children.Add(CreateCard("💾 数据", card =>
            {
                var btnPanel = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
                btnPanel.Children.Add(CreateSmallButton("📤 导出JSON", ExportJson_Click));
                btnPanel.Children.Add(CreateSmallButton("📤 导出CSV", ExportCsv_Click));
                btnPanel.Children.Add(CreateSmallButton("📥 导入", Import_Click));
                btnPanel.Children.Add(CreateSmallButton("💾 备份", Backup_Click));
                card.Children.Add(btnPanel);
            }));

            // 关于
            stack.Children.Add(CreateCard("ℹ️ 关于", card =>
            {
                card.Children.Add(new TextBlock
                {
                    Text = $"版本 {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "4.0.0"} · 每日任务",
                    FontSize = 12,
                    Foreground = GetBrush("TextSecondaryBrush", Color.FromRgb(100, 116, 139))
                });
            }));

            scrollViewer.Content = stack;
            Grid.SetRow(scrollViewer, 1);

            // ===== 底部 =====
            var footer = new Border
            {
                Background = GetBrush("GlassLightBrush", Color.FromRgb(240, 242, 248)),
                Padding = new Thickness(20, 12, 20, 12),
                CornerRadius = new CornerRadius(0, 0, 14, 14),
                BorderThickness = new Thickness(0, 1, 0, 0),
                BorderBrush = GetBrush("BorderBrush", Color.FromArgb(26, 0, 0, 0))
            };
            var footerBtn = new Button
            {
                Content = "关闭",
                HorizontalAlignment = HorizontalAlignment.Right,
                Padding = new Thickness(28, 6, 28, 6),
                Background = GetBrush("AccentBrush", Color.FromRgb(99, 102, 241)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            // 圆角按钮模板
            var btnTemplate = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            borderFactory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));
            var presenterFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            presenterFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenterFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(presenterFactory);
            btnTemplate.VisualTree = borderFactory;
            footerBtn.Template = btnTemplate;
            footerBtn.Click += (s, e) => Close();
            footer.Child = footerBtn;
            Grid.SetRow(footer, 2);

            grid.Children.Add(header);
            grid.Children.Add(scrollViewer);
            grid.Children.Add(footer);
            mainBorder.Child = grid;
            Content = mainBorder;
        }

        private Border CreateCard(string title, Action<StackPanel> populateContent)
        {
            var card = new Border
            {
                Background = GetBrush("CardBrush", Color.FromArgb(245, 255, 255, 255)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 10),
                BorderThickness = new Thickness(0, 0, 0, 0)
            };

            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = GetBrush("TextBrush", Color.FromRgb(30, 41, 59)),
                Margin = new Thickness(0, 0, 0, 4)
            });

            var separator = new Border
            {
                Height = 1,
                Background = GetBrush("BorderBrush", Color.FromArgb(26, 0, 0, 0)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            panel.Children.Add(separator);

            populateContent(panel);
            card.Child = panel;
            return card;
        }

        private RadioButton CreateThemeRadio(string content, string tag)
        {
            var radio = new RadioButton
            {
                Content = content,
                Tag = tag,
                Margin = new Thickness(0, 0, 16, 0),
                FontSize = 13,
                Foreground = GetBrush("TextBrush", Color.FromRgb(30, 41, 59)),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            radio.Checked += Theme_Changed;
            return radio;
        }

        private Button CreateSmallButton(string content, RoutedEventHandler handler)
        {
            var btn = new Button
            {
                Content = content,
                Margin = new Thickness(0, 0, 8, 6),
                Padding = new Thickness(12, 6, 12, 6),
                FontSize = 12,
                Background = GetBrush("GlassLightBrush", Color.FromRgb(240, 242, 248)),
                Foreground = GetBrush("TextBrush", Color.FromRgb(30, 41, 59)),
                BorderThickness = new Thickness(1),
                BorderBrush = GetBrush("BorderBrush", Color.FromArgb(26, 0, 0, 0)),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            var btnTemplate = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            borderFactory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
            borderFactory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            borderFactory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));
            var presenterFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            presenterFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenterFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(presenterFactory);
            btnTemplate.VisualTree = borderFactory;
            btn.Template = btnTemplate;
            btn.Click += handler;
            return btn;
        }

        private void Theme_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton radio && radio.Tag is string themeStr)
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
