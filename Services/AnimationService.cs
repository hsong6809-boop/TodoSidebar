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
        /// <summary>确保元素有 TransformGroup，返回已注册的位移和缩放变换</summary>
        private static (TranslateTransform translate, ScaleTransform scale) EnsureTransformGroup(FrameworkElement element)
        {
            if (element.RenderTransform is TransformGroup group
                && group.Children.Count >= 2
                && group.Children[0] is TranslateTransform t
                && group.Children[1] is ScaleTransform s)
                return (t, s);

            element.RenderTransformOrigin = new Point(0.5, 0.5);
            var translate = new TranslateTransform();
            var scale = new ScaleTransform(1, 1);
            element.RenderTransform = new TransformGroup
            {
                Children = { translate, scale }
            };
            return (translate, scale);
        }

        /// <summary>启用硬件加速缓存</summary>
        public static void EnableHardwareCache(FrameworkElement element)
        {
            RenderOptions.SetBitmapScalingMode(element, BitmapScalingMode.LowQuality);
            element.CacheMode = new BitmapCache(1.0);
        }

        // 任务添加动画（从右侧滑入）
        public static void AnimateAdd(FrameworkElement element)
        {
            var (translate, _) = EnsureTransformGroup(element);
            translate.X = 100;
            element.Opacity = 0;

            var slideAnimation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var opacityAnimation = new DoubleAnimation(1, TimeSpan.FromMilliseconds(200));

            translate.BeginAnimation(TranslateTransform.XProperty, slideAnimation);
            element.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
        }

        // 任务完成动画（弹跳 + 粒子）
        public static void AnimateComplete(FrameworkElement element, Action? onComplete = null)
        {
            var (_, scale) = EnsureTransformGroup(element);

            var scaleAnim = new DoubleAnimation(1.05, TimeSpan.FromMilliseconds(120))
            {
                AutoReverse = true,
                EasingFunction = new ElasticEase
                {
                    EasingMode = EasingMode.EaseOut,
                    Oscillations = 2,
                    Springiness = 3
                }
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);

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

        // 任务删除动画（向左滑出 + 淡出）
        public static void AnimateDelete(FrameworkElement element, Action? onComplete = null)
        {
            var (translate, _) = EnsureTransformGroup(element);

            var slideAnimation = new DoubleAnimation(-120, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            var opacityAnimation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));

            slideAnimation.Completed += (s, e) => onComplete?.Invoke();

            translate.BeginAnimation(TranslateTransform.XProperty, slideAnimation);
            element.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
        }

        // 按钮点击反馈
        public static void AnimateButtonClick(FrameworkElement element)
        {
            var (_, scale) = EnsureTransformGroup(element);

            var animation = new DoubleAnimation(0.92, TimeSpan.FromMilliseconds(60))
            {
                AutoReverse = true,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }

        // 错误提示动画（抖动）
        public static void AnimateError(FrameworkElement element)
        {
            var (translate, _) = EnsureTransformGroup(element);

            var animation = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(350)
            };
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(6, KeyTime.FromPercent(0.15)));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(-6, KeyTime.FromPercent(0.3)));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(3, KeyTime.FromPercent(0.5)));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(-3, KeyTime.FromPercent(0.7)));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1)));

            translate.BeginAnimation(TranslateTransform.XProperty, animation);
        }

        // 数字变化动画（平滑过渡）
        public static void AnimateNumberChange(TextBlock textBlock, int fromValue, int toValue)
        {
            var animation = new Int32Animation(fromValue, toValue, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            textBlock.BeginAnimation(TextBlock.TextProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }

        // 淡入
        public static void AnimateFadeIn(FrameworkElement element, double durationMs = 200)
        {
            element.Opacity = 0;
            var animation = new DoubleAnimation(1, TimeSpan.FromMilliseconds(durationMs))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            element.BeginAnimation(UIElement.OpacityProperty, animation);
        }

        // 淡出
        public static void AnimateFadeOut(FrameworkElement element, double durationMs = 200, Action? onComplete = null)
        {
            var animation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(durationMs))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            animation.Completed += (s, e) => onComplete?.Invoke();
            element.BeginAnimation(UIElement.OpacityProperty, animation);
        }

        // 悬停效果（平滑过渡）
        public static void ApplyHoverEffect(FrameworkElement element)
        {
            var (_, scale) = EnsureTransformGroup(element);

            element.MouseEnter += (s, e) =>
            {
                var anim = new DoubleAnimation(1.02, TimeSpan.FromMilliseconds(150))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
            };

            element.MouseLeave += (s, e) =>
            {
                var anim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
            };
        }

        // 粒子效果
        public static void CreateCompletionParticles(Canvas canvas, Point position)
        {
            var random = new Random();

            for (int i = 0; i < 8; i++)
            {
                var particle = new Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = new SolidColorBrush(Color.FromRgb(99, 102, 241)),
                    Opacity = 1
                };

                Canvas.SetLeft(particle, position.X);
                Canvas.SetTop(particle, position.Y);
                canvas.Children.Add(particle);

                var angle = random.NextDouble() * Math.PI * 2;
                var distance = random.Next(30, 60);

                var xAnimation = new DoubleAnimation(
                    position.X,
                    position.X + Math.Cos(angle) * distance,
                    TimeSpan.FromMilliseconds(400))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                var yAnimation = new DoubleAnimation(
                    position.Y,
                    position.Y + Math.Sin(angle) * distance - 20,
                    TimeSpan.FromMilliseconds(400))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                var opacityAnimation = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(350));

                xAnimation.Completed += (s, e) => canvas.Children.Remove(particle);
                particle.BeginAnimation(Canvas.LeftProperty, xAnimation);
                particle.BeginAnimation(Canvas.TopProperty, yAnimation);
                particle.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
            }
        }
    }
}
