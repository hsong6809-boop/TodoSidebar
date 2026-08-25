using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using TodoSidebar.Models;
using TodoSidebar.ViewModels;

namespace TodoSidebar.Converters
{
    /// <summary>
    /// v5.3 热力图色阶转换：HeatmapDay → 刷子。
    /// 空白格透明；L0 取 SurfacePressedBrush；L1~L4 取 AccentBrush 颜色按透明度递增。
    /// 主题/强调色切换后由统计页重新加载数据触发重取。
    /// </summary>
    public class HeatmapLevelToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not HeatmapDay day)
                return Brushes.Transparent;
            if (day.IsBlank)
                return Brushes.Transparent;

            if (day.Level <= 0)
                return TryFindBrush("SurfacePressedBrush", new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)));

            var accent = TryFindBrush("AccentBrush", new SolidColorBrush(Color.FromRgb(0x5B, 0x5F, 0xE9)));
            var color = accent is SolidColorBrush sc ? sc.Color : Color.FromRgb(0x5B, 0x5F, 0xE9);
            var alpha = day.Level switch
            {
                1 => (byte)0x55,
                2 => (byte)0x88,
                3 => (byte)0xBB,
                _ => (byte)0xFF
            };
            var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
            brush.Freeze();
            return brush;
        }

        private static Brush TryFindBrush(string key, Brush fallback)
        {
            var found = Application.Current?.TryFindResource(key) as Brush;
            return found ?? fallback;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
