using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
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
        private string _currentAccent = "Indigo";
        private readonly DatabaseService _dbService;

        /// <summary>
        /// V2-W2：可用强调色板（名称 / 浅色主题基准色 / 深色主题基准色）。
        /// 其余变体（Hover/Pressed/Soft/Light/渐变）按明暗主题自动推导。
        /// </summary>
        public static readonly (string Name, string LightHex, string DarkHex)[] AccentPalettes =
        {
            ("Indigo", "#6366F1", "#818CF8"),
            ("Ocean",  "#0284C7", "#38BDF8"),
            ("Sunset", "#EA580C", "#FB923C"),
            ("Forest", "#059669", "#34D399"),
            ("Mono",   "#334155", "#94A3B8"),
        };

        /// <summary>当前强调色名称（持久化于设置表 Accent 键）。</summary>
        public string CurrentAccent
        {
            get => _currentAccent;
            set
            {
                var name = NormalizeAccent(value);
                if (_currentAccent == name) return;
                _currentAccent = name;
                ApplyAccent(IsCurrentlyDark());
                try { _dbService.SetSetting("Accent", name); } catch (Exception ex) { Debug.WriteLine($"ThemeManager: 保存强调色失败: {ex.Message}"); }
            }
        }

        /// <summary>取色板的当前主题基准色。</summary>
        public static System.Windows.Media.Color GetAccentBase(string accentName)
        {
            foreach (var p in AccentPalettes)
            {
                if (string.Equals(p.Name, accentName, StringComparison.OrdinalIgnoreCase))
                    return FromHex(IsCurrentlyDark() ? p.DarkHex : p.LightHex);
            }
            return FromHex(AccentPalettes[0].LightHex);
        }

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

            // V5.1 修复：必须先读持久化强调色、再应用主题。
            // 原顺序 LoadThemePreference() 在前，ApplyTheme→ApplyAccent 会以默认 Indigo 上屏，
            // 随后读到的已保存颜色只改字段不再渲染 => 重启必回紫色；
            // 且此时 setter 的同名短路使重选同色无效，表现为"自己弹回默认"。
            try
            {
                var savedAccent = _dbService.GetSetting("Accent");
                if (!string.IsNullOrWhiteSpace(savedAccent)) _currentAccent = NormalizeAccent(savedAccent);
            }
            catch (Exception ex) { Debug.WriteLine($"ThemeManager: 读取强调色失败: {ex.Message}"); }

            LoadThemePreference();

            // M33：监听系统主题变化，"跟随系统"模式下实时响应明暗切换
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }

        private static string NormalizeAccent(string value)
        {
            foreach (var p in AccentPalettes)
            {
                if (string.Equals(p.Name, value, StringComparison.OrdinalIgnoreCase))
                    return p.Name;
            }
            return AccentPalettes[0].Name;
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

            // V2-W2：令牌就绪后叠加强调色（根字典值优先于合并字典）
            ApplyAccent(theme == ThemeType.Dark);

            ThemeChanged?.Invoke(this, theme);
        }

        /// <summary>
        /// V2-W2：将强调色系刷子写入应用根资源（覆盖令牌字典中的默认 Indigo 值）。
        /// 按明暗主题自动推导 Hover/Pressed/Light/Soft/Glow/渐变变体。
        /// </summary>
        public void ApplyAccent(bool dark)
        {
            var app = Application.Current;
            if (app == null) return;

            try
            {
                var baseHex = Array.Find(AccentPalettes, p => p.Name == _currentAccent);
                var c = FromHex(dark ? baseHex.DarkHex : baseHex.LightHex);

                Color hover = dark ? Lighten(c, 0.14) : Darken(c, 0.10);
                Color pressed = dark ? Lighten(c, 0.28) : Darken(c, 0.20);
                Color lighter = Lighten(c, dark ? 0.26 : 0.18);
                byte softAlpha = dark ? (byte)0x29 : (byte)0x1F;

                SetBrush(app, "AccentBrush", c);
                SetBrush(app, "PrimaryBrush", c);
                SetBrush(app, "TypeDailyBrush", c);
                SetBrush(app, "AccentLightBrush", lighter);
                SetBrush(app, "AccentHoverBrush", hover);
                SetBrush(app, "AccentPressedBrush", pressed);
                SetBrush(app, "AccentSoftBrush", System.Windows.Media.Color.FromArgb(softAlpha, c.R, c.G, c.B));
                app.Resources["AccentGlowColor"] = c;
                app.Resources["AccentGradientBrush"] = new System.Windows.Media.LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1),
                    GradientStops =
                    {
                        new System.Windows.Media.GradientStop(c, 0),
                        new System.Windows.Media.GradientStop(Lighten(c, dark ? 0.30 : 0.28), 1)
                    }
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ThemeManager: 应用强调色失败: {ex.Message}");
            }
        }

        private static void SetBrush(Application app, string key, System.Windows.Media.Color color)
        {
            var brush = new System.Windows.Media.SolidColorBrush(color);
            brush.Freeze();
            app.Resources[key] = brush;
        }

        private static System.Windows.Media.Color FromHex(string hex) =>
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);

        private static System.Windows.Media.Color Lighten(System.Windows.Media.Color c, double amount) => Shift(c, amount);

        private static System.Windows.Media.Color Darken(System.Windows.Media.Color c, double amount) => Shift(c, -amount);

        private static System.Windows.Media.Color Shift(System.Windows.Media.Color c, double amount)
        {
            if (amount >= 0)
            {
                byte L(byte v) => (byte)(v + (255 - v) * amount);
                return System.Windows.Media.Color.FromRgb(L(c.R), L(c.G), L(c.B));
            }
            else
            {
                var k = 1 + amount;
                byte D(byte v) => (byte)(v * k);
                return System.Windows.Media.Color.FromRgb(D(c.R), D(c.G), D(c.B));
            }
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
