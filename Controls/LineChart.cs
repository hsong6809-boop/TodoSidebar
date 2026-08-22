using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace TodoSidebar.Controls
{
    /// <summary>
    /// V2-W6 轻量折线图（自绘，无第三方库）：平滑单序列曲线 + 面积填充 + 端点圆点。
    /// 颜色取应用令牌（AccentBrush / AccentSoftBrush），随主题自动变化。
    /// </summary>
    public class LineChart : FrameworkElement
    {
        public static readonly DependencyProperty ValuesProperty =
            DependencyProperty.Register(nameof(Values), typeof(System.Collections.IEnumerable),
                typeof(LineChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>数据序列。</summary>
        public System.Collections.IEnumerable Values
        {
            get => (System.Collections.IEnumerable)GetValue(ValuesProperty);
            set => SetValue(ValuesProperty, value);
        }

        private static readonly SolidColorBrush FallbackAccent =
            new SolidColorBrush(Color.FromRgb(0x63, 0x66, 0xF1));

        protected override Size MeasureOverride(Size availableSize)
        {
            var w = double.IsNaN(availableSize.Width) ? 200 : Math.Max(80, availableSize.Width);
            return new Size(w, 120);
        }

        protected override void OnRender(DrawingContext dc)
        {
            var values = ExtractValues();
            if (values.Count < 2) return;

            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0 || double.IsNaN(w) || double.IsNaN(h)) return;

            const double padL = 6, padR = 12, padT = 12, padB = 8;
            double plotW = w - padL - padR, plotH = h - padT - padB;

            double max = 0;
            foreach (var v in values) max = Math.Max(max, v);
            if (max <= 0) max = 1;

            Point Pt(int i)
            {
                var x = padL + i * plotW / (values.Count - 1);
                var y = padT + (1 - values[i] / max) * plotH;
                return new Point(x, y);
            }

            var accent = TryFindResource("AccentBrush") as SolidColorBrush ?? FallbackAccent;
            var soft = TryFindResource("AccentSoftBrush") as Brush
                ?? new SolidColorBrush(Color.FromArgb(0x28, accent.Color.R, accent.Color.G, accent.Color.B));

            // 网格基线（三道）
            var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(0x18, 0x64, 0x74, 0x8B)), 1);
            gridPen.Freeze();
            for (int g = 1; g <= 3; g++)
            {
                var y = padT + plotH * g / 3;
                dc.DrawLine(gridPen, new Point(padL, y), new Point(w - padR, y));
            }

            // 面积填充
            var area = new StreamGeometry();
            using (var ctx = area.Open())
            {
                ctx.BeginFigure(Pt(0), true, true);
                for (int i = 1; i < values.Count; i++) ctx.LineTo(Pt(i), true, false);
                ctx.LineTo(new Point(padL + plotW, padT + plotH), true, false);
                ctx.LineTo(new Point(padL, padT + plotH), true, false);
            }
            area.Freeze();
            dc.DrawGeometry(soft, null, area);

            // 折线
            var line = new StreamGeometry();
            using (var ctx = line.Open())
            {
                ctx.BeginFigure(Pt(0), false, false);
                for (int i = 1; i < values.Count; i++) ctx.LineTo(Pt(i), true, false);
            }
            line.Freeze();

            var stroke = new Pen(accent, 2.4)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            stroke.Freeze();
            dc.DrawGeometry(null, stroke, line);

            // 端点
            var last = Pt(values.Count - 1);
            var white = new SolidColorBrush(Colors.White); white.Freeze();
            dc.DrawEllipse(white, null, last, 4, 4);
            dc.DrawEllipse(accent, null, last, 2.8, 2.8);
        }

        private List<double> ExtractValues()
        {
            var result = new List<double>();
            if (Values is System.Collections.IEnumerable src)
            {
                foreach (var item in src)
                {
                    switch (item)
                    {
                        case double d: result.Add(d); break;
                        case int i: result.Add(i); break;
                        case float f: result.Add(f); break;
                    }
                }
            }
            return result;
        }
    }
}
