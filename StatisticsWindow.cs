using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using TodoSidebar.Services;
using TodoSidebar.ViewModels;

namespace TodoSidebar
{
    public class StatisticsWindow : Window
    {
        private readonly StatisticsViewModel _viewModel;

        public StatisticsWindow()
        {
            _viewModel = new StatisticsViewModel(DatabaseService.Instance);
            DataContext = _viewModel;
            InitializeUI();
        }

        /// <summary>
        /// 安全获取资源刷子：失败时记录日志并回退到 fallback。
        /// </summary>
        private static Brush TryGetBrush(string key, Brush fallback)
        {
            try
            {
                return (Brush)Application.Current.Resources[key] ?? fallback;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"StatisticsWindow: 获取资源 {key} 失败: {ex.Message}");
                return fallback;
            }
        }

        /// <summary>
        /// 解析十六进制颜色，失败返回 null（避免 catch 内二次抛异常）。
        /// </summary>
        private static Brush? TryParseColor(string? color)
        {
            if (string.IsNullOrWhiteSpace(color)) return null;
            try
            {
                if (ColorConverter.ConvertFromString(color) is Color c)
                {
                    var brush = new SolidColorBrush(c);
                    brush.Freeze();
                    return brush;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"StatisticsWindow: 无效颜色 {color}: {ex.Message}");
            }
            return null;
        }

        private void InitializeUI()
        {
            Title = "数据统计";
            Width = 500;
            Height = 600;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            try
            {
                Background = (Brush)FindResource("GlassBrush");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"StatisticsWindow: GlassBrush 获取失败: {ex.Message}");
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"StatisticsWindow: header 资源获取失败: {ex.Message}");
                header.Background = new SolidColorBrush(Color.FromRgb(245, 245, 250));
                header.BorderBrush = new SolidColorBrush(Color.FromArgb(50, 0, 0, 0));
            }

            header.Child = new TextBlock
            {
                Text = "📊 数据统计",
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

            // 总体概览
            stack.Children.Add(CreateStatCard("📈 总体概览",
                CreateStatGrid(
                    ("总任务", _viewModel.TotalTasks.ToString()),
                    ("已完成", _viewModel.CompletedTasks.ToString()),
                    ("完成率", $"{_viewModel.CompletionRate:P0}")
                )));

            // 今日统计
            stack.Children.Add(CreateStatCard("📅 今日统计",
                CreateStatGrid(
                    ("今日任务", _viewModel.TodayTotal.ToString()),
                    ("已完成", _viewModel.TodayCompleted.ToString()),
                    ("完成率", $"{_viewModel.TodayCompletionRate:P0}")
                )));

            // 特殊统计
            stack.Children.Add(CreateStatCard("⭐ 特殊统计",
                CreateStatGrid(
                    ("过期任务", _viewModel.OverdueTasks.ToString(), _viewModel.OverdueTasks > 0 ? "#FF5A5A" : null),
                    ("连续天数", $"{_viewModel.StreakDays}天", "#FFB800")
                )));

            // 专注统计（番茄钟）
            var (pomoCompleted, pomoInterrupted, focusMinutes) = PomodoroService.Instance.GetTodayStats();
            stack.Children.Add(CreateStatCard("🍅 专注统计",
                CreateStatGrid(
                    ("今日番茄", pomoCompleted.ToString(), pomoCompleted >= PomodoroService.DailyTarget ? "#10B981" : null),
                    ("专注分钟", focusMinutes.ToString(), (string?)null),
                    ("中断", pomoInterrupted.ToString(), pomoInterrupted > 0 ? "#FF5A5A" : null)
                )));

            // 成长曲线（近 7 天每日经验）
            stack.Children.Add(CreateStatCard("📈 成长曲线（近 7 天经验）", CreateGrowthChart()));

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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"StatisticsWindow: footer 资源获取失败: {ex.Message}");
                footer.Background = new SolidColorBrush(Color.FromRgb(245, 245, 250));
                footer.BorderBrush = new SolidColorBrush(Color.FromArgb(50, 0, 0, 0));
            }

            var refreshBtn = new Button
            {
                Content = "🔄 刷新",
                Padding = new Thickness(15, 8, 15, 8),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            refreshBtn.Click += (s, e) => { _viewModel.LoadStatistics(); };

            var closeBtn = new Button
            {
                Content = "关闭",
                Padding = new Thickness(25, 8, 25, 8),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            closeBtn.Click += (s, e) => Close();

            var footerPanel = new DockPanel();
            DockPanel.SetDock(refreshBtn, Dock.Left);
            footerPanel.Children.Add(refreshBtn);
            footerPanel.Children.Add(closeBtn);
            footer.Child = footerPanel;
            Grid.SetRow(footer, 2);

            mainGrid.Children.Add(header);
            mainGrid.Children.Add(content);
            mainGrid.Children.Add(footer);

            Content = mainGrid;
        }

        private Border CreateStatCard(string title, UIElement content)
        {
            var card = new Border
            {
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(15),
                CornerRadius = new CornerRadius(8)
            };

            try
            {
                card.Background = (Brush)FindResource("CardBrush");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"StatisticsWindow: CardBrush 获取失败: {ex.Message}");
                card.Background = Brushes.White;
            }

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10)
            });
            stack.Children.Add(content);
            card.Child = stack;

            return card;
        }

        private Grid CreateStatGrid(params (string label, string value, string? color)[] items)
        {
            var grid = new Grid();
            
            foreach (var item in items)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition());
            }

            for (int i = 0; i < items.Length; i++)
            {
                var (label, value, color) = items[i];
                
                var stack = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(5)
                };

                var valueBlock = new TextBlock
                {
                    Text = value,
                    FontSize = 22,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                // 颜色解析失败时回退默认色，避免 catch 内二次抛异常
                var parsedColor = TryParseColor(color);
                if (parsedColor != null)
                {
                    valueBlock.Foreground = parsedColor;
                }
                else
                {
                    valueBlock.Foreground = TryGetBrush("AccentBrush", new SolidColorBrush(Color.FromRgb(91, 95, 233)));
                }

                var labelBlock = new TextBlock
                {
                    Text = label,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                try
                {
                    labelBlock.Foreground = (Brush)FindResource("TextSecondaryBrush");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"StatisticsWindow: TextSecondaryBrush 获取失败: {ex.Message}");
                    labelBlock.Foreground = Brushes.Gray;
                }

                stack.Children.Add(valueBlock);
                stack.Children.Add(labelBlock);

                Grid.SetColumn(stack, i);
                grid.Children.Add(stack);
            }

            return grid;
        }

        // 重载方法，支持只有两个统计项的情况
        private Grid CreateStatGrid(params (string label, string value)[] items)
        {
            var tuples = new (string label, string value, string? color)[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                tuples[i] = (items[i].label, items[i].value, null);
            }
            return CreateStatGrid(tuples);
        }

        /// <summary>
        /// 近 7 天每日经验折线图（Canvas 自绘，无第三方图表依赖）。
        /// </summary>
        private FrameworkElement CreateGrowthChart()
        {
            var data = DatabaseService.Instance.GetDailyXpLastDays(7);
            const double chartWidth = 430;
            const double chartHeight = 110;
            const double left = 20, right = 20, top = 8, bottom = 22;

            var canvas = new Canvas
            {
                Width = chartWidth,
                Height = chartHeight,
                Margin = new Thickness(0, 6, 0, 0)
            };

            if (data.Count < 2)
            {
                canvas.Children.Add(new TextBlock
                {
                    Text = "完成任务积累更多经验后，这里会出现你的成长曲线",
                    FontSize = 11,
                    Foreground = Brushes.Gray,
                    Margin = new Thickness(left, top + 20, 0, 0)
                });
                return canvas;
            }

            var maxXp = Math.Max(1, data.Max(d => d.xp));
            var plotW = chartWidth - left - right;
            var plotH = chartHeight - top - bottom;
            var points = new List<Point>();

            for (int i = 0; i < data.Count; i++)
            {
                double x = left + i * plotW / (data.Count - 1);
                double y = top + plotH - (data[i].xp / (double)maxXp) * plotH;
                points.Add(new Point(x, y));

                // 数据点
                var dot = new Ellipse
                {
                    Width = 5,
                    Height = 5,
                    Fill = new SolidColorBrush(Color.FromRgb(99, 102, 241)),
                    Stroke = Brushes.White,
                    StrokeThickness = 1
                };
                Canvas.SetLeft(dot, x - 2.5);
                Canvas.SetTop(dot, y - 2.5);
                canvas.Children.Add(dot);

                // 日期标签
                var label = new TextBlock
                {
                    Text = data[i].date.ToString("MM/dd"),
                    FontSize = 10,
                    Foreground = Brushes.Gray
                };
                Canvas.SetLeft(label, x - 14);
                Canvas.SetTop(label, chartHeight - 18);
                canvas.Children.Add(label);
            }

            // 折线
            var polyline = new Polyline
            {
                Points = new PointCollection(points),
                Stroke = new SolidColorBrush(Color.FromRgb(99, 102, 241)),
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round
            };
            canvas.Children.Add(polyline);

            return canvas;
        }
    }
}
