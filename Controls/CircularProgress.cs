using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TodoSidebar.Controls
{
    public class CircularProgress : Control
    {
        public static readonly DependencyProperty ProgressProperty =
            DependencyProperty.Register("Progress", typeof(double), typeof(CircularProgress),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnProgressChanged));

        public static readonly DependencyProperty StrokeThicknessProperty =
            DependencyProperty.Register("StrokeThickness", typeof(double), typeof(CircularProgress),
                new FrameworkPropertyMetadata(4.0, FrameworkPropertyMetadataOptions.AffectsRender, OnPenAffectingPropertyChanged));

        public static readonly DependencyProperty ProgressBrushProperty =
            DependencyProperty.Register("ProgressBrush", typeof(Brush), typeof(CircularProgress),
                new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender, OnPenAffectingPropertyChanged));

        public static readonly DependencyProperty BackgroundBrushProperty =
            DependencyProperty.Register("BackgroundBrush", typeof(Brush), typeof(CircularProgress),
                new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender, OnPenAffectingPropertyChanged));

        public static readonly DependencyProperty SizeProperty =
            DependencyProperty.Register("Size", typeof(double), typeof(CircularProgress),
                new FrameworkPropertyMetadata(60.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public double Progress
        {
            get => (double)GetValue(ProgressProperty);
            set => SetValue(ProgressProperty, Math.Max(0, Math.Min(1, value)));
        }

        public Brush ProgressBrush
        {
            get => (Brush)GetValue(ProgressBrushProperty);
            set => SetValue(ProgressBrushProperty, value);
        }

        public Brush BackgroundBrush
        {
            get => (Brush)GetValue(BackgroundBrushProperty);
            set => SetValue(BackgroundBrushProperty, value);
        }

        public double Size
        {
            get => (double)GetValue(SizeProperty);
            set => SetValue(SizeProperty, value);
        }

        public double StrokeThickness
        {
            get => (double)GetValue(StrokeThicknessProperty);
            set => SetValue(StrokeThicknessProperty, value);
        }

        static CircularProgress()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(CircularProgress),
                new FrameworkPropertyMetadata(typeof(CircularProgress)));
        }

        private static void OnProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CircularProgress control)
            {
                // 绑定绕过 CLR setter 直接 SetValue，此处统一钳制到 [0,1]
                var value = (double)e.NewValue;
                if (value < 0 || value > 1)
                {
                    control.SetValue(ProgressProperty, Math.Clamp(value, 0, 1));
                    return;
                }
                control.InvalidateCachedPens();
                control.InvalidateVisual();
            }
        }

        /// <summary>
        /// 笔刷/线宽变化时重建缓存 Pen（M26）：否则主题切换或样式调整后，
        /// 旧 Pen 仍持有失效的 Brush 引用，圆环颜色不会更新。
        /// </summary>
        private static void OnPenAffectingPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CircularProgress control)
            {
                control.InvalidateCachedPens();
                control.InvalidateVisual();
            }
        }

        private Pen? _cachedBackgroundPen;
        private Pen? _cachedProgressPen;

        private void InvalidateCachedPens()
        {
            _cachedBackgroundPen = null;
            _cachedProgressPen = null;
        }

        /// <summary>
        /// 声明期望尺寸 = Size（修复无模板 Control 默认尺寸为 0，
        /// 导致环形与相邻元素重叠的布局问题）。
        /// </summary>
        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(Size, Size);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            // 保护：StrokeThickness 大于 Size 时半径为负，绘制会异常
            var radius = Math.Max(0, (Size - StrokeThickness) / 2);
            var center = new Point(Size / 2, Size / 2);

            // 缓存 Pen 避免每帧创建
            if (_cachedBackgroundPen == null)
            {
                _cachedBackgroundPen = new Pen(BackgroundBrush, StrokeThickness);
                _cachedBackgroundPen.Freeze();
            }

            // 绘制背景圆
            drawingContext.DrawEllipse(null, _cachedBackgroundPen, center, radius, radius);

            // 绘制进度弧
            if (Progress > 0)
            {
                if (_cachedProgressPen == null)
                {
                    _cachedProgressPen = new Pen(ProgressBrush, StrokeThickness);
                    _cachedProgressPen.StartLineCap = PenLineCap.Round;
                    _cachedProgressPen.EndLineCap = PenLineCap.Round;
                    _cachedProgressPen.Freeze();
                }

                var angle = Progress * 360;

                // 进度满格（>=360°）时弧段起终点重合导致圆环消失（M27），直接绘制整圆
                if (angle >= 360)
                {
                    drawingContext.DrawEllipse(null, _cachedProgressPen, center, radius, radius);
                }
                else
                {
                    var startPoint = new Point(
                        center.X + radius * Math.Cos(-Math.PI / 2),
                        center.Y + radius * Math.Sin(-Math.PI / 2));

                    var endPoint = new Point(
                        center.X + radius * Math.Cos((-Math.PI / 2) + (angle * Math.PI / 180)),
                        center.Y + radius * Math.Sin((-Math.PI / 2) + (angle * Math.PI / 180)));

                    var isLargeArc = angle > 180;

                    var pathFigure = new PathFigure
                    {
                        StartPoint = startPoint,
                        IsClosed = false
                    };

                    pathFigure.Segments.Add(new ArcSegment
                    {
                        Point = endPoint,
                        Size = new Size(radius, radius),
                        IsLargeArc = isLargeArc,
                        SweepDirection = SweepDirection.Clockwise,
                        RotationAngle = 0
                    });

                    var pathGeometry = new PathGeometry();
                    pathGeometry.Figures.Add(pathFigure);

                    drawingContext.DrawGeometry(null, _cachedProgressPen, pathGeometry);
                }
            }
        }
    }
}
