using System;

namespace TodoSidebar.Services
{
    /// <summary>
    /// 主题管理接口。
    /// </summary>
    public interface IThemeManager
    {
        /// <summary>当前主题类型</summary>
        ThemeType CurrentTheme { get; set; }

        /// <summary>主题变化事件</summary>
        event EventHandler<ThemeType>? ThemeChanged;
    }
}
