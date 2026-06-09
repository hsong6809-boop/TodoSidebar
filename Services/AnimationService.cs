using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace TodoSidebar.Services
{
    public static class AnimationService
    {
        // 任务添加动画（从右侧滑入）
        public static void AnimateAdd(FrameworkElement element)
        {
            element.RenderTransform = new TranslateTransform(100, 0);
            element.Opacity = 0;

            var slideAnimation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var opacityAnimation = new DoubleAnimation(1, TimeSpan.FromMilliseconds(200));

            element.RenderTransform.BeginAnimation(TranslateTransform.XProperty, slideAnimation);
            element.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
        }

        // 任务完成动画（划线 + 缩放）
        public static void AnimateComplete(FrameworkElement element, Action? onComplete = null)
        {
            // 缩放动画
            var scaleTransform = new ScaleTransform(1, 1);
            element.RenderTransform = scaleTransform;
            element.RenderTransformOrigin = new Point(0.5, 0.5);

            var scaleXAnimation = new DoubleAnimation(1.05, TimeSpan.FromMilliseconds(100))
            {
                AutoReverse = true,
                EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut }
            };

            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnimation);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleXAnimation);

            // 延迟后执行完成回调
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                onComplete?.Invoke();
            };
            timer.Start();
        }

        // 任务删除动画（向左滑出）
        public static void AnimateDelete(FrameworkElement element, Action? onComplete = null)
        {
            var slideAnimation = new DoubleAnimation(-100, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            var opacityAnimation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));

            slideAnimation.Completed += (s, e) => onComplete?.Invoke();

            element.RenderTransform = new TranslateTransform(0, 0);
            element.RenderTransform.BeginAnimation(TranslateTransform.XProperty, slideAnimation);
            element.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
        }

        // 按钮点击反馈
        public static void AnimateButtonClick(FrameworkElement element)
        {
            var scaleTransform = new ScaleTransform(1, 1);
            element.RenderTransform = scaleTransform;
            element.RenderTransformOrigin = new Point(0.5, 0.5);

            var animation = new DoubleAnimation(0.95, TimeSpan.FromMilliseconds(50))
            {
                AutoReverse = true
            };

            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }

        // 错误提示动画（抖动）
        public static void AnimateError(FrameworkElement element)
        {
            var translateTransform = new TranslateTransform(0, 0);
            element.RenderTransform = translateTransform;

            var animation = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(400)
            };

            animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(5, KeyTime.FromPercent(0.2)));
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(-5, KeyTime.FromPercent(0.4)));
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(3, KeyTime.FromPercent(0.6)));
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(-3, KeyTime.FromPercent(0.8)));
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));

            translateTransform.BeginAnimation(TranslateTransform.XProperty, animation);
        }

        // 数字变化动画
        public static void AnimateNumberChange(TextBlock textBlock, int fromValue, int toValue)
        {
            var animation = new Int32Animation(fromValue, toValue, TimeSpan.FromMilliseconds(300));
            // 注意：TextBlock 没有直接的数字动画，需要用属性动画
            textBlock.Text = toValue.ToString();
        }

        // 淡入动画
        public static void AnimateFadeIn(FrameworkElement element, double duration = 200)
        {
            element.Opacity = 0;
            var animation = new DoubleAnimation(1, TimeSpan.FromMilliseconds(duration));
            element.BeginAnimation(UIElement.OpacityProperty, animation);
        }

        // 淡出动画
        public static void AnimateFadeOut(FrameworkElement element, double duration = 200, Action? onComplete = null)
        {
            var animation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(duration));
            animation.Completed += (s, e) => onComplete?.Invoke();
            element.BeginAnimation(UIElement.OpacityProperty, animation);
        }

        // 悬停效果
        public static void ApplyHoverEffect(UIElement element)
        {
            element.MouseEnter += (s, e) =>
            {
                if (element is FrameworkElement fe)
                {
                    var scaleTransform = new ScaleTransform(1.02, 1.02);
                    fe.RenderTransform = scaleTransform;
                    fe.RenderTransformOrigin = new Point(0.5, 0.5);
                }
            };

            element.MouseLeave += (s, e) =>
            {
                if (element is FrameworkElement fe)
                {
                    var scaleTransform = new ScaleTransform(1, 1);
                    fe.RenderTransform = scaleTransform;
                }
            };
        }

        // 创建粒子效果（任务完成时）
        public static void CreateCompletionParticles(Canvas canvas, Point position)
        {
            var random = new Random();

            for (int i = 0; i < 8; i++)
            {
                var particle = new Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = new SolidColorBrush(Color.FromRgb(0, 196, 140)),
                    Opacity = 1
                };

                Canvas.SetLeft(particle, position.X);
                Canvas.SetTop(particle, position.Y);
                canvas.Children.Add(particle);

                // 随机方向动画
                var angle = random.NextDouble() * Math.PI * 2;
                var distance = random.Next(30, 60);

                var xAnimation = new DoubleAnimation(
                    position.X,
                    position.X + Math.Cos(angle) * distance,
                    TimeSpan.FromMilliseconds(400));

                var yAnimation = new DoubleAnimation(
                    position.Y,
                    position.Y + Math.Sin(angle) * distance,
                    TimeSpan.FromMilliseconds(400));

                var opacityAnimation = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400));

                xAnimation.Completed += (s, e) => canvas.Children.Remove(particle);

                particle.BeginAnimation(Canvas.LeftProperty, xAnimation);
                particle.BeginAnimation(Canvas.TopProperty, yAnimation);
                particle.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
            }
        }
    }
}
