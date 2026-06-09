using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

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

        public double StrokeThickness
        {
            get => (double)GetValue(StrokeThicknessProperty);
            set => SetValue(StrokeThicknessProperty, value);
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

        static CircularProgress()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(CircularProgress),
                new FrameworkPropertyMetadata(typeof(CircularProgress)));
        }

        private static void OnProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CircularProgress control)
            {
                control.InvalidateVisual();
            }
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            var radius = (Size - StrokeThickness) / 2;
            var center = new Point(Size / 2, Size / 2);

            // 绘制背景圆
            var backgroundPen = new Pen(BackgroundBrush, StrokeThickness);
            drawingContext.DrawEllipse(null, backgroundPen, center, radius, radius);

            // 绘制进度弧
            if (Progress > 0)
            {
                var progressPen = new Pen(ProgressBrush, StrokeThickness);
                progressPen.StartLineCap = PenLineCap.Round;
                progressPen.EndLineCap = PenLineCap.Round;

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

                drawingContext.DrawGeometry(null, progressPen, pathGeometry);
            }
        }
    }
}
