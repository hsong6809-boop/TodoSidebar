using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
            catch
            {
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

                try
                {
                    valueBlock.Foreground = color != null
                        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(color))
                        : (Brush)FindResource("AccentBrush");
                }
                catch
                {
                    valueBlock.Foreground = color != null
                        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(color))
                        : new SolidColorBrush(Color.FromRgb(91, 95, 233));
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
                catch
                {
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
    }
}
