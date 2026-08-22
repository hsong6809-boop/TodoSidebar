using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace TodoSidebar.Controls
{
    /// <summary>
    /// V2-W2 阴影分级（设计令牌的代码形态）：
    /// 级别 1 = 卡片悬浮；2 = 下拉/浮层；3 = 模态弹窗。0/其他 = 清除。
    /// 用法：controls:Elevation.Level="2"
    /// </summary>
    public static class Elevation
    {
        public static readonly DependencyProperty LevelProperty =
            DependencyProperty.RegisterAttached("Level", typeof(int), typeof(Elevation),
                new PropertyMetadata(0, OnLevelChanged));

        public static int GetLevel(DependencyObject obj) => (int)obj.GetValue(LevelProperty);
        public static void SetLevel(DependencyObject obj, int value) => obj.SetValue(LevelProperty, value);

        private static void OnLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not UIElement element) return;

            element.Effect = (int)e.NewValue switch
            {
                1 => Create(10, 0.07),
                2 => Create(18, 0.13),
                3 => Create(26, 0.20),
                _ => null
            };
        }

        private static DropShadowEffect Create(double blur, double opacity)
        {
            var effect = new DropShadowEffect
            {
                BlurRadius = blur,
                ShadowDepth = 2,
                Direction = 270,
                Opacity = opacity,
                Color = Colors.Black
            };
            effect.Freeze();
            return effect;
        }
    }
}
