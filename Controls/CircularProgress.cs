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
                new PropertyMetadata(0.0, OnProgressChanged));

        public static readonly DependencyProperty StrokeThicknessProperty =
            DependencyProperty.Register("StrokeThickness", typeof(double), typeof(CircularProgress),
                new PropertyMetadata(4.0));

        public static readonly DependencyProperty ProgressBrushProperty =
            DependencyProperty.Register("ProgressBrush", typeof(Brush), typeof(CircularProgress),
                new PropertyMetadata(Brushes.DodgerBlue));

        public static readonly DependencyProperty BackgroundBrushProperty =
            DependencyProperty.Register("BackgroundBrush", typeof(Brush), typeof(CircularProgress),
                new PropertyMetadata(Brushes.LightGray));

        public static readonly DependencyProperty SizeProperty =
            DependencyProperty.Register("Size", typeof(double), typeof(CircularProgress),
                new PropertyMetadata(60.0));

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

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            var radius = (Size - StrokeThickness) / 2;
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
