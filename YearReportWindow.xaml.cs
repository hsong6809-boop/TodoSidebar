using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using TodoSidebar.Services;

namespace TodoSidebar
{
    /// <summary>
    /// v5.5 年度报告：聚合当年完成/专注/连击数据渲染分享卡，
    /// 支持切换年份与 RenderTargetBitmap 导出 PNG（2x 清晰度）。
    /// </summary>
    public partial class YearReportWindow : Window
    {
        private int _year;

        public YearReportWindow(int year)
        {
            InitializeComponent();
            _year = year;
            LoadReport(_year);
        }

        private void LoadReport(int year)
        {
            _year = year;
            YearText.Text = year.ToString();
            CardTitle.Text = $"{year} · 年度报告";

            try
            {
                var counts = DatabaseService.Instance.GetHeatmapCounts(
                    new DateTime(year, 1, 1), new DateTime(year, 12, 31));

                int total = counts.Values.Sum();
                int activeDays = counts.Count(kv => kv.Value > 0);

                // 最勤奋的星期（按完成次数加权）
                string busiest = "—";
                if (total > 0)
                {
                    var weekdayTotals = new double[7];
                    foreach (var kv in counts)
                    {
                        if (DateTime.TryParseExact(kv.Key, "yyyy-MM-dd",
                                CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                            weekdayTotals[(int)d.DayOfWeek] += kv.Value;
                    }
                    int bestIdx = 0;
                    for (int i = 1; i < 7; i++)
                        if (weekdayTotals[i] > weekdayTotals[bestIdx]) bestIdx = i;
                    busiest = "周" + "日一二三四五六"[bestIdx];
                }

                // 番茄专注
                var (pomos, minutes) = DatabaseService.Instance.GetPomodoroYearSummary(year);
                var combo = LevelService.Instance.GetGrowth().BestComboDays;

                HeroCount.Text = total.ToString("N0");
                BestComboText.Text = $"{combo} 天";
                FocusMinutesText.Text = minutes >= 60
                    ? $"{minutes / 60} 小时 {minutes % 60} 分"
                    : $"{minutes} 分钟";
                ActiveDaysText.Text = $"{activeDays} 天";
                BusiestWeekdayText.Text = busiest;

                // 月度趋势（12 个值）
                var monthly = new List<double>(12);
                for (int m = 1; m <= 12; m++)
                {
                    var prefix = $"{year:0000}-{m:00}";
                    monthly.Add(counts.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
                                       .Sum(kv => (double)kv.Value));
                }
                MonthChart.Values = monthly;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"YearReport load error: {ex.Message}");
            }
        }

        private static readonly int MinYear = 2020; // v5.5 审查修复：历史下界，防止导航到 0/负数年

        private void PrevYear_Click(object sender, RoutedEventArgs e)
        {
            if (_year > MinYear) LoadReport(_year - 1);
        }

        private void NextYear_Click(object sender, RoutedEventArgs e)
        {
            if (_year < DateTime.Today.Year) LoadReport(_year + 1);
        }

        /// <summary>v5.5：报告卡导出 PNG（RenderTargetBitmap 2x 缩放）。</summary>
        private void SaveImage_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "PNG 图片|*.png",
                FileName = $"年度报告_{_year}.png"
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                ReportCard.UpdateLayout();
                var w = ReportCard.ActualWidth;
                var h = ReportCard.ActualHeight;
                if (w <= 0 || h <= 0) throw new InvalidOperationException("卡片尚未完成布局");

                const double scale = 2.0;
                var rtb = new RenderTargetBitmap(
                    (int)Math.Round(w * scale), (int)Math.Round(h * scale),
                    96 * scale, 96 * scale, System.Windows.Media.PixelFormats.Pbgra32);

                // v5.5 审查修复：Light 主题 CardBrush 为半透明白（α≈96%），
                // 直出 PNG 贴深色背景会透出灰底 —— 临时替换为不透明同色再渲染，然后还原
                var originalBg = ReportCard.Background;
                if (originalBg is System.Windows.Media.SolidColorBrush sc && sc.Color.A < 255)
                {
                    ReportCard.Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromArgb(255, sc.Color.R, sc.Color.G, sc.Color.B));
                }
                try
                {
                    rtb.Render(ReportCard);
                }
                finally
                {
                    ReportCard.Background = originalBg;
                }

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using var stream = File.Create(dialog.FileName);
                encoder.Save(stream);

                MessageBox.Show(this, $"已保存到：\n{dialog.FileName}", "导出成功",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"导出失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try { DragMove(); } catch { }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
