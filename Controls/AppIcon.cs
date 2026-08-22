using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TodoSidebar.Controls
{
    /// <summary>
    /// 矢量图标控件：基于 Segoe Fluent Icons（Win11）/ Segoe MDL2 Assets（Win10）字体渲染字形，
    /// 可继承父级 Foreground，双主题一致。用法：
    /// <code>&lt;controls:AppIcon Glyph="{x:Static controls:Icons.Search}"/&gt;</code>
    /// </summary>
    public class AppIcon : TextBlock
    {
        public static readonly DependencyProperty GlyphProperty =
            DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(AppIcon),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender, OnGlyphChanged));

        /// <summary>图标字形码位（单字符字符串）。</summary>
        public string Glyph
        {
            get => (string)GetValue(GlyphProperty);
            set => SetValue(GlyphProperty, value);
        }

        static AppIcon()
        {
            FontFamilyProperty.OverrideMetadata(typeof(AppIcon), new FrameworkPropertyMetadata(
                new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets")));
            TextAlignmentProperty.OverrideMetadata(typeof(AppIcon), new FrameworkPropertyMetadata(TextAlignment.Center));
            TextOptions.TextFormattingModeProperty.OverrideMetadata(typeof(AppIcon),
                new FrameworkPropertyMetadata(TextFormattingMode.Display));
        }

        private static void OnGlyphChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AppIcon icon)
            {
                icon.Text = e.NewValue as string ?? string.Empty;
            }
        }
    }

    /// <summary>
    /// Segoe MDL2 Assets 图标码位常量（与官方清单一致；Fluent Icons 同码位兼容）。
    /// </summary>
    public static class Icons
    {
        public const string Search = "\uE721";          // 搜索
        public const string Settings = "\uE713";        // 设置
        public const string SignOut = "\uF3B1";         // 退出登录
        public const string ChevronLeft = "\uE76B";     // 收起
        public const string ChevronRight = "\uE76C";
        public const string ChevronUp = "\uE70E";
        public const string ChevronDown = "\uE70D";
        public const string OpenInNewWindow = "\uE8A7"; // 展开完整窗口
        public const string CheckMark = "\uE73E";       // 完成
        public const string Delete = "\uE74D";          // 删除
        public const string Add = "\uE710";             // 添加
        public const string ChromeClose = "\uE8BB";     // 关闭
        public const string Refresh = "\uE72C";         // 刷新/同步
        public const string Upload = "\uE898";          // 上传
        public const string Download = "\uE896";        // 下载
        public const string Play = "\uE768";            // 播放
        public const string Pause = "\uE769";           // 暂停
        public const string Stop = "\uE71A";            // 停止
        public const string Calendar = "\uE787";        // 日历
        public const string Pin = "\uE718";             // 项目/置顶
        public const string Timer = "\uE916";           // 计时器
        public const string Recent = "\uE823";          // 最近/时钟
        public const string Diagnostic = "\uE9D2";      // 统计图表
        public const string Market = "\uE719";          // 趋势
        public const string FavoriteStar = "\uE734";    // 收藏星（成就）
        public const string FavoriteStarFill = "\uE735";
        public const string CheckList = "\uE9D5";       // 子任务清单
        public const string RedEye = "\uE7B3";          // 显示密码
        public const string Hide = "\uED1A";            // 隐藏密码
        public const string Info = "\uE946";            // 关于
        public const string Save = "\uE74E";            // 保存
        public const string ColorBg = "\uE790";         // 主题颜色
        public const string Mail = "\uE715";            // 邮箱
        public const string PasswordKey = "\uE192";     // 密码
        public const string Lock = "\uE72E";            // 安全
        public const string Lightbulb = "\uEA80";       // 提示
        public const string More = "\uE712";            // 更多
        public const string Restore = "\uE8A8";         // 恢复
        public const string Filter = "\uE71C";          // 筛选
    }
}
