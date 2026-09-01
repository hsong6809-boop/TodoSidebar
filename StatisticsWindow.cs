using System;
using System.Collections.Generic;
using System.Globalization;
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

        // 动态区域容器与卡片引用（L18）：供「刷新」按钮清除旧卡片后重建
        private StackPanel? _dynamicCardsHost;
        private Border? _focusStatsCard;
        private Border? _growthChartCard;
        private Border? _typingCard;

        // ===== 输入统计卡（云同步 + 日/周/月/年聚合）=====
        private int _typingPeriod; // 0=今日 1=本周 2=本月 3=今年
        private static readonly string[] TypingPeriodNames = { "今日", "本周", "本月", "今年" };

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

            // 专注统计与成长曲线依赖实时数据，提为可重建方法（L18）：
            // 初始化与「刷新」按钮共用，追加在静态卡片之后
            _dynamicCardsHost = stack;
            RefreshDynamicCards();

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
            refreshBtn.Click += (s, e) =>
            {
                _viewModel.LoadStatistics();
                RefreshDynamicCards(); // 同步重建专注统计卡与成长曲线，避免陈旧数据
            };

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

        /// <summary>
        /// 重建依赖实时数据的三张卡片（专注统计、成长曲线、输入统计）：
        /// 先移除旧卡片再重新构建，初始化与「刷新」按钮共用（L18）。
        /// </summary>
        private void RefreshDynamicCards()
        {
            if (_dynamicCardsHost == null) return;

            // 清理旧卡片（若存在），避免刷新后重复叠加
            if (_focusStatsCard != null) _dynamicCardsHost.Children.Remove(_focusStatsCard);
            if (_growthChartCard != null) _dynamicCardsHost.Children.Remove(_growthChartCard);
            if (_typingCard != null) _dynamicCardsHost.Children.Remove(_typingCard);

            // 专注统计（番茄钟）
            var (pomoCompleted, pomoInterrupted, focusMinutes) = PomodoroService.Instance.GetTodayStats();
            _focusStatsCard = CreateStatCard("🍅 专注统计",
                CreateStatGrid(
                    ("今日番茄", pomoCompleted.ToString(), pomoCompleted >= PomodoroService.DailyTarget ? "#10B981" : null),
                    ("专注分钟", focusMinutes.ToString(), (string?)null),
                    ("中断", pomoInterrupted.ToString(), pomoInterrupted > 0 ? "#FF5A5A" : null)
                ));
            _dynamicCardsHost.Children.Add(_focusStatsCard);

            // 成长曲线（近 7 天每日经验）
            _growthChartCard = CreateStatCard("📈 成长曲线（近 7 天经验）", CreateGrowthChart());
            _dynamicCardsHost.Children.Add(_growthChartCard);

            // 输入统计（今日/本周/本月/今年 + 每日趋势）
            _typingCard = CreateTypingCard();
            _dynamicCardsHost.Children.Add(_typingCard);
        }

        /// <summary>
        /// 输入统计卡：周期切换（今日/本周/本月/今年）+ 汇总指标 + 趋势柱状图。
        /// 数据源为本地 DailyTypingStat（登录后经云同步多端合并）。
        /// </summary>
        private Border CreateTypingCard()
        {
            var card = new Border
            {
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(15),
                CornerRadius = new CornerRadius(8)
            };

            try { card.Background = (Brush)FindResource("CardBrush"); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"StatisticsWindow: CardBrush 获取失败: {ex.Message}");
                card.Background = Brushes.White;
            }

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = "⌨️ 输入统计",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var body = new StackPanel();

            // 周期切换按钮行
            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 10)
            };
            var buttons = new List<Button>();
            for (int i = 0; i < TypingPeriodNames.Length; i++)
            {
                var idx = i;
                var btn = new Button
                {
                    Content = TypingPeriodNames[i],
                    FontSize = 11,
                    Padding = new Thickness(12, 4, 12, 4),
                    Margin = new Thickness(0, 0, 6, 0),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                btn.Click += (_, _) =>
                {
                    _typingPeriod = idx;
                    RenderTypingBody(body);
                    HighlightTypingButtons(buttons);
                };
                buttons.Add(btn);
                btnRow.Children.Add(btn);
            }

            stack.Children.Add(btnRow);
            stack.Children.Add(body);
            card.Child = stack;

            RenderTypingBody(body);
            HighlightTypingButtons(buttons);
            return card;
        }

        /// <summary>按当前周期重算并渲染输入统计卡正文（指标行 + 趋势图）。</summary>
        private void RenderTypingBody(StackPanel body)
        {
            body.Children.Clear();
            try
            {
                var today = DateTime.Today;
                var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7)); // 周一为一周起点
                var monthStart = new DateTime(today.Year, today.Month, 1);
                var yearStart = new DateTime(today.Year, 1, 1);

                // 统计区间（左）与图表区间（右；"今日"用最近 7 天做趋势，今年按 12 个月聚合）
                (DateTime statStart, DateTime chartStart, bool monthly) = _typingPeriod switch
                {
                    0 => (today, today.AddDays(-6), false),
                    1 => (weekStart, weekStart, false),
                    2 => (monthStart, monthStart, false),
                    _ => (yearStart, yearStart, true)
                };
                var statEnd = today;
                var chartEnd = today;

                // 先冲刷内存增量落库（未启用时为空操作），保证"今日"数值最新
                try { TypingStatsService.Instance.FlushNow(); } catch { /* 尽力而为 */ }

                var statMap = DatabaseService.Instance.GetTypingStatsRange(statStart, statEnd);
                long keys = 0, words = 0;
                int activeDays = 0;
                foreach (var v in statMap.Values)
                {
                    keys += v.KeyStrokes;
                    words += v.WordChars;
                    if (v.KeyStrokes > 0 || v.WordChars > 0) activeDays++;
                }

                int spanDays = (int)(statEnd - statStart).TotalDays + 1;
                double avgWords = spanDays > 0 ? (double)words / spanDays : 0;

                body.Children.Add(CreateStatGrid(
                    ("估算字数", words.ToString("N0", CultureInfo.InvariantCulture), null),
                    ("击键", keys.ToString("N0", CultureInfo.InvariantCulture), null),
                    ("日均字数", avgWords >= 1 ? avgWords.ToString("N0", CultureInfo.InvariantCulture) : "0", null),
                    ("活跃天数", $"{activeDays}/{spanDays}", null)
                ));

                // 趋势图（区间与统计区间相同则直接复用数据，避免重复查库）
                var chartMap = (_typingPeriod == 0)
                    ? DatabaseService.Instance.GetTypingStatsRange(chartStart, chartEnd)
                    : statMap;
                body.Children.Add(CreateTypingChart(chartMap, chartStart, chartEnd, monthly));

                // 功能未开启且全周期无数据时给出引导
                if (keys == 0 && words == 0 &&
                    DatabaseService.Instance.GetSetting("TypingStatsEnabled") != "true")
                {
                    body.Children.Add(new TextBlock
                    {
                        Text = "输入统计未开启 · 可在设置中打开",
                        FontSize = 10,
                        Foreground = Brushes.Gray,
                        Margin = new Thickness(0, 4, 0, 0)
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RenderTypingBody error: {ex.Message}");
                body.Children.Add(new TextBlock
                {
                    Text = "加载失败",
                    FontSize = 11,
                    Foreground = Brushes.Gray
                });
            }
        }

        /// <summary>
        /// 输入趋势柱状图（Canvas 自绘，无第三方依赖）：按日或按月聚合估算字数。
        /// </summary>
        private FrameworkElement CreateTypingChart(
            Dictionary<string, (int KeyStrokes, int WordChars)> map,
            DateTime start, DateTime end, bool monthly)
        {
            const double chartWidth = 430;
            const double chartHeight = 100;
            const double left = 4, right = 4, top = 6, bottom = 16;

            var canvas = new Canvas
            {
                Width = chartWidth,
                Height = chartHeight,
                Margin = new Thickness(0, 6, 0, 0)
            };

            // 值/标签/提示序列：今年=12 个月，其余=逐日
            var values = new List<double>();
            var labels = new List<string>();
            var tips = new List<string>();
            if (monthly)
            {
                for (int m = 1; m <= 12; m++)
                {
                    var prefix = $"{start.Year:0000}-{m:00}";
                    double v = map.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
                                  .Sum(kv => (double)kv.Value.WordChars);
                    values.Add(v);
                    labels.Add($"{m}月");
                    tips.Add($"{start.Year} 年 {m} 月 · 约 {v:N0} 字");
                }
            }
            else
            {
                for (var d = start; d <= end; d = d.AddDays(1))
                {
                    var key = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    double v = map.TryGetValue(key, out var t) ? t.WordChars : 0;
                    values.Add(v);
                    labels.Add($"{d.Month}/{d.Day}");
                    tips.Add($"{d:M月d日} · 约 {v:N0} 字");
                }
            }

            if (values.All(v => v <= 0))
            {
                canvas.Children.Add(new TextBlock
                {
                    Text = "该周期暂无输入数据",
                    FontSize = 11,
                    Foreground = Brushes.Gray,
                    Margin = new Thickness(left, top + 20, 0, 0)
                });
                return canvas;
            }

            double max = Math.Max(1, values.Max());
            int n = values.Count;
            double slot = (chartWidth - left - right) / n;
            double barW = Math.Max(2, slot * 0.62);
            double plotH = chartHeight - top - bottom;
            var accent = TryGetBrush("AccentBrush", new SolidColorBrush(Color.FromRgb(91, 95, 233)));

            for (int i = 0; i < n; i++)
            {
                double barH = values[i] / max * plotH;
                var rect = new Rectangle
                {
                    Width = barW,
                    Height = values[i] > 0 ? Math.Max(2, barH) : 1,
                    RadiusX = 2,
                    RadiusY = 2,
                    Fill = accent,
                    Opacity = values[i] > 0 ? 0.85 : 0.15,
                    ToolTip = tips[i]
                };
                double x = left + i * slot + (slot - barW) / 2;
                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, top + plotH - rect.Height);
                canvas.Children.Add(rect);

                // 标签稀疏化：逐日超过 16 根时每 5 根 + 最后一根标注
                bool showLabel = n <= 16 || i % 5 == 0 || i == n - 1;
                if (showLabel)
                {
                    var label = new TextBlock
                    {
                        Text = labels[i],
                        FontSize = 9,
                        Foreground = Brushes.Gray
                    };
                    Canvas.SetLeft(label, Math.Min(x - 4, chartWidth - 30));
                    Canvas.SetTop(label, chartHeight - 14);
                    canvas.Children.Add(label);
                }
            }

            return canvas;
        }

        /// <summary>高亮输入统计卡当前选中的周期按钮。</summary>
        private void HighlightTypingButtons(List<Button> buttons)
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                bool selected = i == _typingPeriod;
                buttons[i].Background = selected
                    ? TryGetBrush("AccentBrush", new SolidColorBrush(Color.FromRgb(91, 95, 233)))
                    : Brushes.Transparent;
                buttons[i].Foreground = selected
                    ? Brushes.White
                    : TryGetBrush("TextSecondaryBrush", Brushes.Gray);
            }
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
