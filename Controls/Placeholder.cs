using System.Windows;

namespace TodoSidebar.Controls
{
    /// <summary>
    /// 输入框占位提示：为 TextBox 提供 Placeholder 能力（由隐式 TextBox 模板渲染）。
    /// 用法：<code>controls:Placeholder.Text="邮箱"</code>
    /// </summary>
    public static class Placeholder
    {
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.RegisterAttached("Text", typeof(string), typeof(Placeholder),
                new FrameworkPropertyMetadata(string.Empty));

        public static string GetText(DependencyObject obj) => (string)obj.GetValue(TextProperty);
        public static void SetText(DependencyObject obj, string value) => obj.SetValue(TextProperty, value);
    }
}
