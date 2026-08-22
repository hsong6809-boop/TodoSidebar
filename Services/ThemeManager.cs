using System;
using System.Windows;
using Microsoft.Win32;
using TodoSidebar.Services;

namespace TodoSidebar.Services
{
    public enum ThemeType
    {
        Light,
        Dark,
        System
    }

    /// <summary>
    /// 主题管理器：通过整体替换 Tokens.Light / Tokens.Dark 资源字典实现换肤，
    /// 所有 DynamicResource 引用自动刷新。对外 API（CurrentTheme/ApplyTheme/ThemeChanged）保持不变。
    /// </summary>
    public class ThemeManager : IThemeManager
    {
        private static ThemeManager? _instance;
        public static ThemeManager Instance => _instance ??= new ThemeManager();

        private const string LightTokenMarker = "tokens.light.xaml";
        private const string DarkTokenMarker = "tokens.dark.xaml";

        private ThemeType _currentTheme = ThemeType.Light;
        private readonly DatabaseService _dbService;

        public ThemeType CurrentTheme
        {
            get => _currentTheme;
            set
            {
                if (_currentTheme == value) return;
                _currentTheme = value;
                ApplyTheme(value);
                SaveThemePreference(value);
            }
        }

        public event EventHandler<ThemeType>? ThemeChanged;

        /// <summary>
        /// 当前实际生效的是否为深色（解析"跟随系统"）。供外壳亚克力等视觉集成使用。
        /// </summary>
        public static bool IsCurrentlyDark()
        {
            var t = Instance._currentTheme;
            if (t == ThemeType.System) t = IsSystemDarkTheme() ? ThemeType.Dark : ThemeType.Light;
            return t == ThemeType.Dark;
        }

        private ThemeManager()
        {
            _dbService = DatabaseService.Instance;
            LoadThemePreference();

            // M33：监听系统主题变化，"跟随系统"模式下实时响应明暗切换
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }

        /// <summary>
        /// M33：系统外观偏好变化回调。仅处理常规类别（含应用主题颜色），
        /// 且当前为"跟随系统"时才重新应用；事件来自系统广播线程，必须封送回 UI 线程。
        /// </summary>
        private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General) return;
            if (_currentTheme != ThemeType.System) return;

            var app = Application.Current;
            if (app == null) return;

            app.Dispatcher.Invoke(() => ApplyTheme(ThemeType.System));
        }

        private void LoadThemePreference()
        {
            var savedTheme = _dbService.GetSetting("Theme");
            if (savedTheme != null && Enum.TryParse<ThemeType>(savedTheme, out var theme))
            {
                _currentTheme = theme;
            }
            ApplyTheme(_currentTheme);
        }

        private void SaveThemePreference(ThemeType theme)
        {
            _dbService.SetSetting("Theme", theme.ToString());
        }

        /// <summary>
        /// 检测 Windows 系统当前使用的主题
        /// </summary>
        private static bool IsSystemDarkTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key?.GetValue("AppsUseLightTheme") is int value)
                    return value == 0;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"ThemeManager: 读取系统主题注册表失败: {ex.Message}"); }
            return false;
        }

        public void ApplyTheme(ThemeType theme)
        {
            var app = Application.Current;
            if (app == null) return;

            if (theme == ThemeType.System)
            {
                theme = IsSystemDarkTheme() ? ThemeType.Dark : ThemeType.Light;
            }

            try
            {
                SwapTokenDictionary(app.Resources.MergedDictionaries, theme == ThemeType.Dark);

                // 自检：令牌必须可解析，否则界面会因 DynamicResource 全部落空而透明不可见
                if (app.TryFindResource("GlassBrush") is not System.Windows.Media.SolidColorBrush)
                {
                    System.Diagnostics.Debug.WriteLine("ThemeManager: 令牌字典替换后 GlassBrush 缺失！");
                }
            }
            catch (Exception ex)
            {
                // 替换失败时保留现有字典（至少启动时的 Light 字典仍然完整）
                System.Diagnostics.Debug.WriteLine($"ThemeManager: 主题字典替换失败: {ex.Message}");
            }

            ThemeChanged?.Invoke(this, theme);
        }

        /// <summary>
        /// 将合并字典中的颜色令牌字典整本替换为对应主题版本（原位替换，保持字典顺序）。
        /// 使用绝对 pack URI，避免代码内相对 URI 在无 BAML 基址上下文时解析失败。
        /// </summary>
        private static void SwapTokenDictionary(System.Collections.ObjectModel.Collection<ResourceDictionary> mergedDictionaries, bool dark)
        {
            var fileName = dark ? "Tokens.Dark.xaml" : "Tokens.Light.xaml";
            var newSource = new Uri($"pack://application:,,,/Themes/{fileName}");

            for (int i = 0; i < mergedDictionaries.Count; i++)
            {
                var src = mergedDictionaries[i].Source?.OriginalString;
                if (src != null &&
                    (src.EndsWith(LightTokenMarker, StringComparison.OrdinalIgnoreCase) ||
                     src.EndsWith(DarkTokenMarker, StringComparison.OrdinalIgnoreCase)))
                {
                    mergedDictionaries[i] = new ResourceDictionary { Source = newSource };
                    return;
                }
            }

            // 未找到令牌字典（异常配置）：插入到最前，保证样式可覆盖
            mergedDictionaries.Insert(0, new ResourceDictionary { Source = newSource });
        }
    }
}
