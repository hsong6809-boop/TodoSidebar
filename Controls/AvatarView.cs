using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TodoSidebar.Controls
{
    /// <summary>
    /// v5.2 头像视图（三态圆形渲染，自绘零模板）：
    ///   custom  → ImageSource 圆形裁切铺满；
    ///   d1~d8   → 内置渐变底 + 白色矢量图形（AvatarCatalog）；
    ///   兜底    → FallbackText 首字符；无字符时通用人形图形。
    /// 尺寸跟随布局的 Width/Height（取短边为直径），42/28/88px 均可复用。
    /// </summary>
    public class AvatarView : Control
    {
        public static readonly DependencyProperty KindProperty =
            DependencyProperty.Register(nameof(Kind), typeof(string), typeof(AvatarView),
                new FrameworkPropertyMetadata("d1", FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ImageSourceProperty =
            DependencyProperty.Register(nameof(ImageSource), typeof(ImageSource), typeof(AvatarView),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty FallbackTextProperty =
            DependencyProperty.Register(nameof(FallbackText), typeof(string), typeof(AvatarView),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>头像类型：custom / d1~d8 / 其他值走兜底。</summary>
        public string Kind
        {
            get => (string)GetValue(KindProperty);
            set => SetValue(KindProperty, value);
        }

        /// <summary>自定义头像位图（kind=custom 时使用）。</summary>
        public ImageSource ImageSource
        {
            get => (ImageSource)GetValue(ImageSourceProperty);
            set => SetValue(ImageSourceProperty, value);
        }

        /// <summary>兜底显示的首字符来源（昵称/邮箱前缀）。</summary>
        public string FallbackText
        {
            get => (string)GetValue(FallbackTextProperty);
            set => SetValue(FallbackTextProperty, value);
        }

        static AvatarView()
        {
            FocusableProperty.OverrideMetadata(typeof(AvatarView),
                new FrameworkPropertyMetadata(false));
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            // 无显式尺寸时按父级给到的最小边成方形
            var s = Math.Min(
                double.IsNaN(availableSize.Width) ? double.PositiveInfinity : availableSize.Width,
                double.IsNaN(availableSize.Height) ? double.PositiveInfinity : availableSize.Height);
            if (!double.IsFinite(s)) s = 32;
            return new Size(s, s);
        }

        protected override void OnRender(DrawingContext dc)
        {
            var w = ActualWidth <= 0 ? Width : ActualWidth;
            var h = ActualHeight <= 0 ? Height : ActualHeight;
            var size = Math.Min(w, h);
            if (size <= 0 || double.IsNaN(size)) return;

            var cx = RenderSize.Width / 2;
            var cy = RenderSize.Height / 2;
            var r = size / 2;
            var center = new Point(cx, cy);
            var rect = new Rect(cx - r, cy - r, size, size);

            var kind = Kind ?? string.Empty;
            var isCustom = string.Equals(kind, AvatarCatalog.CustomKind, StringComparison.OrdinalIgnoreCase)
                           && ImageSource != null;

            Geometry clip;
            if (isCustom)
            {
                // 自定义头像：圆形裁切铺满位图
                clip = new EllipseGeometry(rect);
                clip.Freeze();
                dc.PushClip(clip);
                var maxSide = Math.Max(ImageSource!.Width, ImageSource.Height);
                var scale = size / maxSide;
                var drawW = ImageSource.Width * scale;
                var drawH = ImageSource.Height * scale;
                dc.DrawImage(ImageSource, new Rect(cx - drawW / 2, cy - drawH / 2, drawW, drawH));
                dc.Pop();
            }
            else
            {
                var builtIn = AvatarCatalog.Resolve(kind);
                var background = builtIn != null
                    ? AvatarCatalog.Gradient(builtIn)
                    : CreateFallbackGradient();

                dc.DrawEllipse(background, null, center, r, r);

                if (builtIn != null)
                {
                    // 内置头像：渐变底 + 白色矢量图形
                    var def = IconPaths.Resolve(builtIn.GlyphKey);
                    if (def != null) DrawGlyph(dc, center, size, def, isFallback: false);
                }
                else
                {
                    // 未知类型：昵称/邮箱首字符兜底；无字符时通用人形图形
                    var initial = (FallbackText ?? string.Empty).Trim();
                    if (initial.Length > 0)
                        DrawInitialLetter(dc, center, size, initial[..1]);
                    else
                    {
                        var personDef = IconPaths.Resolve(AvatarCatalog.PersonGlyph);
                        if (personDef != null) DrawGlyph(dc, center, size, personDef, isFallback: true);
                    }
                }
            }

            // 内缘高光细环（半透明白，增强圆形轮廓在浅色背景上的辨识度）
            var rimPen = new Pen(new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)), Math.Max(1, size * 0.035));
            rimPen.Freeze();
            dc.DrawEllipse(null, rimPen, center, r - rimPen.Thickness / 2, r - rimPen.Thickness / 2);
        }

        private void DrawGlyph(DrawingContext dc, Point center, double size, IconData def, bool isFallback)
        {
            var glyphSize = size * (isFallback ? 0.62 : 0.54);
            var scale = glyphSize / 24.0;
            var fg = new SolidColorBrush(isFallback ? Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF) : Colors.White);
            fg.Freeze();

            dc.PushTransform(new TranslateTransform(center.X - glyphSize / 2, center.Y - glyphSize / 2));
            dc.PushTransform(new ScaleTransform(scale, scale));

            if (def.Filled)
            {
                dc.DrawGeometry(fg, null, def.Geometry);
            }
            else
            {
                var pen = new Pen(fg, Math.Max(1.4, 24 * scale * 0.14))
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

        private void DrawInitialLetter(DrawingContext dc, Point center, double size, string letter)
        {
            var initial = letter.ToUpperInvariant();

            var fontSize = size * 0.44;
            var text = new FormattedText(initial, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal,
                    FontWeights.Bold, FontStretches.Normal),
                fontSize, Brushes.White, 1.25);
            dc.DrawText(text, new Point(center.X - text.WidthIncludingTrailingWhitespace / 2,
                center.Y - text.Height / 2));
        }

        private static LinearGradientBrush CreateFallbackGradient()
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                {
                    new GradientStop(Color.FromRgb(0x94, 0xA3, 0xB8), 0),
                    new GradientStop(Color.FromRgb(0x64, 0x74, 0x8B), 1)
                }
            };
            brush.Freeze();
            return brush;
        }
    }
}
