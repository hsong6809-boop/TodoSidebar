using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TodoSidebar
{
    /// <summary>
    /// 对话框窗口基类，提供通用的标题栏、底部按钮等
    /// </summary>
    public abstract class BaseDialogWindow : Window
    {
        protected BaseDialogWindow()
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = (Brush)Application.Current.Resources["GlassBrush"];
        }

        /// <summary>
        /// 创建标准标题栏
        /// </summary>
        protected Border CreateHeader(string title, string icon = "")
        {
            var header = new Border
            {
                Background = (Brush)Application.Current.Resources["GlassLightBrush"],
                Padding = new Thickness(20, 16, 20, 16),
                BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(0, 0, 0, 1)
            };

            var titleBlock = new TextBlock
            {
                Text = string.IsNullOrEmpty(icon) ? title : $"{icon} {title}",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)Application.Current.Resources["TextBrush"]
            };

            header.Child = titleBlock;
            return header;
        }

        /// <summary>
        /// 创建标准底部栏
        /// </summary>
        protected Border CreateFooter(string closeText = "关闭")
        {
            var footer = new Border
            {
                Background = (Brush)Application.Current.Resources["GlassLightBrush"],
                Padding = new Thickness(16, 12, 16, 12),
                BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(0, 1, 0, 0)
            };

            var closeBtn = new Button
            {
                Content = closeText,
                Style = (Style)Application.Current.Resources["PrimaryButton"],
                HorizontalAlignment = HorizontalAlignment.Right,
                Padding = new Thickness(20, 8, 20, 8)
            };
            closeBtn.Click += (s, e) => Close();

            footer.Child = closeBtn;
            return footer;
        }

        /// <summary>
        /// 创建设置分组
        /// </summary>
        protected StackPanel CreateSettingGroup(string title, UIElement content)
        {
            var group = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 20)
            };

            var titleBlock = new TextBlock
            {
                Text = title,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["TextBrush"],
                Margin = new Thickness(0, 0, 0, 10)
            };

            group.Children.Add(titleBlock);
            group.Children.Add(content);

            return group;
        }

        /// <summary>
        /// 创建统计卡片
        /// </summary>
        protected Border CreateCard(string title, UIElement content)
        {
            var card = new Border
            {
                Background = (Brush)Application.Current.Resources["CardBrush"],
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 15),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 10,
                    ShadowDepth = 2,
                    Opacity = 0.1
                }
            };

            var stack = new StackPanel();

            var titleBlock = new TextBlock
            {
                Text = title,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["TextBrush"],
                Margin = new Thickness(0, 0, 0, 10)
            };

            stack.Children.Add(titleBlock);
            stack.Children.Add(content);
            card.Child = stack;

            return card;
        }

        /// <summary>
        /// 创建统计项
        /// </summary>
        protected StackPanel CreateStatItem(string label, string value, string? color = null)
        {
            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(5, 5, 5, 5)
            };

            var valueBlock = new TextBlock
            {
                Text = value,
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = color != null
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(color))
                    : (Brush)Application.Current.Resources["AccentBrush"],
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var labelBlock = new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
                HorizontalAlignment = HorizontalAlignment.Center
            };

            stack.Children.Add(valueBlock);
            stack.Children.Add(labelBlock);

            return stack;
        }
    }
}
