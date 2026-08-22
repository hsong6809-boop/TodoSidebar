using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using TodoSidebar.Models;

namespace TodoSidebar.Converters
{
    // 统一颜色常量（与 TaskItem.PriorityColor / App.xaml 资源保持一致）
    internal static class Palette
    {
        // 优先级色
        public static readonly Brush PriorityHigh = Freeze("#EF4444");   // Red-500
        public static readonly Brush PriorityMedium = Freeze("#F59E0B"); // Amber-500
        public static readonly Brush PriorityLow = Freeze("#10B981");    // Emerald-500
        // 截止紧急度色
        public static readonly Brush UrgencyOverdue = Freeze("#FF5A5A");
        public static readonly Brush UrgencyToday = Freeze("#FF9632");
        public static readonly Brush UrgencySoon = Freeze("#FFC832");
        public static readonly Brush UrgencySafe = Freeze("#10B981");
        // 完成状态色
        public static readonly Brush CompletedGreen = Freeze("#10B981");
        public static readonly Brush MutedGray = Freeze("#666680");
        // 子任务未完成文字兜底色（浅深主题均可读的中性灰）
        public static readonly Brush SubTaskTextNeutral = Freeze("#64748B");

        private static Brush Freeze(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
    }

    // 优先级转颜色
    public class PriorityToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is TaskPriority priority
                ? priority switch
                {
                    TaskPriority.High => Palette.PriorityHigh,
                    TaskPriority.Medium => Palette.PriorityMedium,
                    TaskPriority.Low => Palette.PriorityLow,
                    _ => Palette.PriorityMedium
                }
                : Palette.PriorityMedium;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    // 优先级转图标
    public class PriorityToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TaskPriority priority)
            {
                return priority switch
                {
                    TaskPriority.High => "🔴",
                    TaskPriority.Medium => "🟡",
                    TaskPriority.Low => "🟢",
                    _ => "⚪"
                };
            }
            return "⚪";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    // 进度转百分比文本
    public class ProgressToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double progress)
            {
                return $"{(int)(progress * 100)}%";
            }
            return "0%";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    // 布尔转动画可见性
    public class BoolToAnimationConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue && parameter is string animationType)
            {
                return boolValue ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    // 布尔反转转可见性
    public class InvertedBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    // 计数转可见性（0时隐藏）
    public class CountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count)
            {
                return count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    // 截止日期转紧急程度颜色
    public class DeadlineToUrgencyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DateTime deadline)
            {
                var daysLeft = (deadline - DateTime.Now).TotalDays;

                if (daysLeft < 0)
                    return Palette.UrgencyOverdue;
                else if (daysLeft <= 1)
                    return Palette.UrgencyToday;
                else if (daysLeft <= 3)
                    return Palette.UrgencySoon;
                else
                    return Palette.UrgencySafe;
            }
            return Palette.UrgencySafe;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    // 截止日期转紧急程度文本
    public class DeadlineToUrgencyTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DateTime deadline)
            {
                var timeLeft = deadline - DateTime.Now;

                if (timeLeft.TotalDays < 0)
                    return "已过期";
                else if (timeLeft.TotalHours < 1)
                    return $"{(int)timeLeft.TotalMinutes}分钟后";
                else if (timeLeft.TotalHours < 24)
                    return $"{(int)timeLeft.TotalHours}小时后";
                else
                    return $"{(int)timeLeft.TotalDays}天后";
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    // 布尔转完成状态颜色
    public class CompletionStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool isCompleted && isCompleted
                ? Palette.CompletedGreen
                : Palette.MutedGray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    // Null/空字符串转可见性
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return Visibility.Collapsed;
            
            if (value is string str && string.IsNullOrWhiteSpace(str))
                return Visibility.Collapsed;
            
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    // 子任务完成状态转删除线
    public class SubTaskCompletionToStrikethrough : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isCompleted && isCompleted)
                return TextDecorations.Strikethrough;
            return Binding.DoNothing;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    // 子任务完成状态转前景色
    public class SubTaskCompletionToForeground : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isCompleted && isCompleted)
                return Palette.MutedGray;
            return GetUncompletedBrush();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }

        /// <summary>
        /// 未完成子任务文字色：优先取主题资源 TextBrush（随浅/深主题切换），
        /// 取不到再回退 SubTaskText 资源，最后回退中性灰，保证浅色主题下可读。
        /// 注意：主题画刷不做 Freeze，直接返回资源字典中的实例。
        /// </summary>
        private static Brush GetUncompletedBrush()
        {
            var resources = Application.Current?.Resources;
            if (resources?["TextBrush"] is Brush textBrush)
                return textBrush;
            if (resources?["SubTaskText"] is Brush subTaskBrush)
                return subTaskBrush;
            return Palette.SubTaskTextNeutral;
        }
    }

    // 子任务进度转百分比宽度（用于进度条）
    public class SubTaskProgressToWidth : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string json && !string.IsNullOrWhiteSpace(json))
            {
                var subTasks = SubTaskHelper.ParseSubTasks(json);
                if (subTasks.Count == 0) return 0.0;
                var progress = SubTaskHelper.GetProgress(subTasks);
                // 假设最大宽度 200px
                double maxWidth = 200;
                if (parameter is string maxStr && double.TryParse(maxStr, out double parsed))
                    maxWidth = parsed;
                return progress * maxWidth;
            }
            return 0.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
    
    // 数量为0时隐藏（用于今日已完成任务区域）
    public class ZeroToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count)
            {
                return count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    /// <summary>P3：零值反向——为 0 显示（用于空状态占位）。</summary>
    public class ZeroToInvertedVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count)
                return count == 0 ? Visibility.Visible : Visibility.Collapsed;
            // 双精度兜底（如完成率）
            if (value is double d)
                return d <= 0.0001 ? Visibility.Visible : Visibility.Collapsed;
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>P3：数值乘法（value × parameter），用于比例转像素高度等。</summary>
    public class MultiplyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                var factor = System.Convert.ToDouble(parameter, CultureInfo.InvariantCulture);
                var v = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return v * factor;
            }
            catch { return 0d; }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>P3：0~1 比例转百分比文本（0.25 → "25%"）。</summary>
    public class FractionToPercentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                var v = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return $"{Math.Round(v * 100)}%";
            }
            catch { return "0%"; }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>V2：空字符串/ null → Collapsed（连击 chip 等空值隐藏场景）。</summary>
    public class EmptyToCollapsedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
