using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TodoSidebar.Controls
{
    /// <summary>V2-W7：命令面板单项。</summary>
    public sealed class PaletteCommand
    {
        public string Title { get; }
        public string Subtitle { get; }
        public string IconName { get; }
        public Action Run { get; }

        public PaletteCommand(string title, string subtitle, string iconName, Action run)
        {
            Title = title;
            Subtitle = subtitle ?? string.Empty;
            IconName = iconName ?? "More";
            Run = run;
        }

        public bool Matches(string keyword) =>
            string.IsNullOrWhiteSpace(keyword)
            || Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || Subtitle.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// V2-W7：Ctrl+K 命令面板。无边框置顶小窗，输入即过滤，↑↓ 选择、Enter 执行、Esc 关闭。
    /// </summary>
    public partial class CommandPalette : Window
    {
        private readonly List<PaletteCommand> _all;
        private List<PaletteCommand> _visible = new();

        private readonly TextBox _searchBox;
        private readonly ListBox _listBox;

        private CommandPalette(Window owner, IEnumerable<PaletteCommand> commands)
        {
            _all = commands.ToList();

            Width = 480;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = true;

            // 定位：跟随所有者窗口，水平居中、顶部下移 110
            Left = owner.Left + (owner.ActualWidth - Width) / 2;
            Top = owner.Top + 110;

            var root = new Border
            {
                CornerRadius = new CornerRadius(14),
                Background = TryFindResource("CardBrush") as Brush ?? Brushes.White,
                BorderBrush = TryFindResource("BorderStrongBrush") as Brush ?? Brushes.Gray,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 16),
            };
            Elevation.SetLevel(root, 3);

            var stack = new StackPanel();

            // 搜索行
            var searchRow = new Grid { Margin = new Thickness(14, 12, 14, 8) };
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var searchIcon = new AppIcon { Glyph = Icons.Search, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9, 0) };
            _searchBox = new TextBox
            {
                FontSize = 14,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            _searchBox.TextChanged += (_, _) => ApplyFilter();
            Grid.SetColumn(searchIcon, 0);
            Grid.SetColumn(_searchBox, 1);
            searchRow.Children.Add(searchIcon);
            searchRow.Children.Add(_searchBox);
            stack.Children.Add(searchRow);

            stack.Children.Add(new Border
            {
                Height = 1,
                Background = TryFindResource("BorderBrush") as Brush ?? Brushes.LightGray,
                Margin = new Thickness(4, 0, 4, 0),
            });

            // 命令列表
            _listBox = new ListBox
            {
                MaxHeight = 320,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Padding = new Thickness(6),
                Focusable = false,
            };
            stack.Children.Add(_listBox);

            root.Child = stack;
            Content = root;

            SourceInitialized += (_, _) => ApplyFilter();
            Deactivated += (_, _) => Close();
            Loaded += (_, _) => { _searchBox.Focus(); ApplyFilter(); };
            PreviewKeyDown += OnPreviewKeyDown;
        }

        /// <summary>显示命令面板。</summary>
        public static void Show(Window owner, IEnumerable<PaletteCommand> commands)
        {
            var palette = new CommandPalette(owner, commands);
            palette.Show();
        }

        private void ApplyFilter()
        {
            var kw = _searchBox?.Text ?? string.Empty;
            _visible = _all.Where(c => c.Matches(kw)).ToList();

            _listBox.Items.Clear();
            foreach (var cmd in _visible)
            {
                _listBox.Items.Add(BuildItem(cmd));
            }
            if (_visible.Count > 0) _listBox.SelectedIndex = 0;
        }

        private FrameworkElement BuildItem(PaletteCommand cmd)
        {
            var row = new Grid { Margin = new Thickness(2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var icon = new AppIcon
            {
                Glyph = cmd.IconName,
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 12, 0),
            };

            var texts = new StackPanel();
            texts.Children.Add(new TextBlock
            {
                Text = cmd.Title,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = TryFindResource("TextBrush") as Brush ?? Brushes.Black,
            });
            if (!string.IsNullOrEmpty(cmd.Subtitle))
            {
                texts.Children.Add(new TextBlock
                {
                    Text = cmd.Subtitle,
                    FontSize = 10.5,
                    Foreground = TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray,
                });
            }

            Grid.SetColumn(icon, 0);
            Grid.SetColumn(texts, 1);
            row.Children.Add(icon);
            row.Children.Add(texts);

            var host = new Border { Child = row, Cursor = Cursors.Hand };
            host.PreviewMouseLeftButtonDown += (_, _) => Execute(cmd);
            return host;
        }

        private void Execute(PaletteCommand cmd)
        {
            Close();
            try { cmd.Run(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"CommandPalette execute error: {ex.Message}"); }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    Close();
                    e.Handled = true;
                    break;
                case Key.Down:
                    MoveSelection(+1);
                    e.Handled = true;
                    break;
                case Key.Up:
                    MoveSelection(-1);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    if (_visible.Count > 0)
                    {
                        var idx = Math.Clamp(_listBox.SelectedIndex < 0 ? 0 : _listBox.SelectedIndex, 0, _visible.Count - 1);
                        Execute(_visible[idx]);
                    }
                    e.Handled = true;
                    break;
            }
        }

        private void MoveSelection(int delta)
        {
            if (_visible.Count == 0) return;
            var next = Math.Clamp((_listBox.SelectedIndex < 0 ? 0 : _listBox.SelectedIndex) + delta, 0, _visible.Count - 1);
            _listBox.SelectedIndex = next;
            _listBox.ScrollIntoView(_listBox.SelectedItem);
        }
    }

    internal static class IconsExtensions
    {
        /// <summary>名称直通（保持目录键一致）。</summary>
        public static string ResolveName(string name) => name;
    }
}
