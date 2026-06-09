using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using TodoSidebar.Models;

namespace TodoSidebar.Converters
{
    // 优先级转颜色
    public class PriorityToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TaskPriority priority)
            {
                return priority switch
                {
                    TaskPriority.High => new SolidColorBrush(Color.FromRgb(255, 90, 90)),
                    TaskPriority.Medium => new SolidColorBrush(Color.FromRgb(255, 184, 0)),
                    TaskPriority.Low => new SolidColorBrush(Color.FromRgb(0, 196, 140)),
                    _ => new SolidColorBrush(Color.FromRgb(255, 184, 0))
                };
            }
            return new SolidColorBrush(Color.FromRgb(255, 184, 0));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
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
            throw new NotImplementedException();
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
            throw new NotImplementedException();
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
            throw new NotImplementedException();
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
            throw new NotImplementedException();
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
            throw new NotImplementedException();
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
                    return new SolidColorBrush(Color.FromRgb(255, 90, 90));
                else if (daysLeft <= 1)
                    return new SolidColorBrush(Color.FromRgb(255, 150, 50));
                else if (daysLeft <= 3)
                    return new SolidColorBrush(Color.FromRgb(255, 200, 50));
                else
                    return new SolidColorBrush(Color.FromRgb(0, 196, 140));
            }
            return new SolidColorBrush(Color.FromRgb(0, 196, 140));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
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
            throw new NotImplementedException();
        }
    }

    // 布尔转完成状态颜色
    public class CompletionStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isCompleted)
            {
                return isCompleted
                    ? new SolidColorBrush(Color.FromRgb(0, 196, 140))
                    : new SolidColorBrush(Color.FromRgb(102, 102, 128));
            }
            return new SolidColorBrush(Color.FromRgb(102, 102, 128));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
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
            throw new NotImplementedException();
        }
    }

    // 子任务完成状态转删除线
    public class SubTaskCompletionToStrikethrough : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isCompleted && isCompleted)
                return TextDecorations.Strikethrough;
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    // 子任务完成状态转前景色
    public class SubTaskCompletionToForeground : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isCompleted && isCompleted)
                return new SolidColorBrush(Color.FromRgb(102, 102, 128)); // 暗灰色
            return new SolidColorBrush(Color.FromRgb(220, 220, 230)); // 正常文字色
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
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
            throw new NotImplementedException();
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
            throw new NotImplementedException();
        }
    }
}
