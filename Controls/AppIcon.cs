using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TodoSidebar.Controls
{
    /// <summary>
    /// 矢量图标控件 V2（W1）：直接渲染 IconPaths 目录中的自绘 Path 几何，
    /// 不依赖任何系统字体——彻底解决 Segoe 字体缺字/跨机器不一致问题。
    ///
    /// 尺寸沿用 FontSize 属性（语义=图标边长，默认继承环境值）；颜色沿用 Foreground。
    /// 用法：&lt;controls:AppIcon Glyph="{x:Static controls:Icons.Search}"/&gt;
    /// （Icons 常量值即目录名称，与旧用法完全兼容。）
    /// </summary>
    public class AppIcon : Control
    {
        public static readonly DependencyProperty GlyphProperty =
            DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(AppIcon),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>图标名称（IconPaths 目录键）。</summary>
        public string Glyph
        {
            get => (string)GetValue(GlyphProperty);
            set => SetValue(GlyphProperty, value);
        }

        static AppIcon()
        {
            FocusableProperty.OverrideMetadata(typeof(AppIcon),
                new FrameworkPropertyMetadata(false));
            ForegroundProperty.OverrideMetadata(typeof(AppIcon),
                new FrameworkPropertyMetadata(System.Windows.Media.Brushes.Gray));
        }

        // 图标占位尺寸 = FontSize（显式 Width/Height 时以实际尺寸为准）
        protected override Size MeasureOverride(Size availableSize)
        {
            var s = double.IsNaN(FontSize) || FontSize <= 0 ? 16d : Math.Max(8, FontSize);
            return new Size(s, s);
        }

        protected override void OnRender(DrawingContext dc)
        {
            var def = IconPaths.Resolve(Glyph);
            if (def == null) return;

            double w = double.IsNaN(ActualWidth) || ActualWidth <= 0 ? FontSize : ActualWidth;
            double h = double.IsNaN(ActualHeight) || ActualHeight <= 0 ? FontSize : ActualHeight;
            double size = Math.Min(w, h);
            if (size <= 0 || double.IsNaN(size)) size = 16;
            if (double.IsNaN(FontSize) || FontSize <= 0) FontSize = size;

            var fg = Foreground ?? System.Windows.Media.Brushes.Gray;
            double scale = size / 24.0;
            double offsetX = (RenderSize.Width - size) / 2;
            double offsetY = (RenderSize.Height - size) / 2;

            dc.PushTransform(new TranslateTransform(offsetX, offsetY));
            dc.PushTransform(new ScaleTransform(scale, scale));

            if (def.Filled)
            {
                dc.DrawGeometry(fg, null, def.Geometry);
            }
            else
            {
                var pen = new Pen(fg, Math.Max(1.25, size * 0.14))
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round
                };
                pen.Freeze();
                dc.DrawGeometry(null, pen, def.Geometry);
            }

            dc.Pop();
            dc.Pop();
        }
    }
}
