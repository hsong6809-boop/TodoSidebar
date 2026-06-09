using System;
using System.Windows;
using System.Windows.Media;
using TodoSidebar.Services;

namespace TodoSidebar.Services
{
    public enum ThemeType
    {
        Light,
        Dark,
        System
    }

    public class ThemeManager
    {
        private static ThemeManager? _instance;
        public static ThemeManager Instance => _instance ??= new ThemeManager();

        private ThemeType _currentTheme = ThemeType.Light;
        private readonly DatabaseService _dbService;

        public ThemeType CurrentTheme
        {
            get => _currentTheme;
            set
            {
                _currentTheme = value;
                ApplyTheme(value);
                SaveThemePreference(value);
            }
        }

        public event EventHandler<ThemeType>? ThemeChanged;

        private ThemeManager()
        {
            _dbService = DatabaseService.Instance;
            
            LoadThemePreference();
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

        public void ApplyTheme(ThemeType theme)
        {
            var app = Application.Current;
            if (app == null) return;

            var resources = app.Resources;

            if (theme == ThemeType.System)
            {
                // TODO: 检测系统主题
                theme = ThemeType.Light;
            }

            switch (theme)
            {
                case ThemeType.Light:
                    ApplyLightTheme(resources);
                    break;
                case ThemeType.Dark:
                    ApplyDarkTheme(resources);
                    break;
            }

            _currentTheme = theme;
            ThemeChanged?.Invoke(this, theme);
        }

        private void ApplyLightTheme(ResourceDictionary resources)
        {
            // 背景色 - 更柔和的半透明效果
            resources["GlassBrush"] = new SolidColorBrush(ColorFromHex("#CCF8F9FE"));  // 更柔和的蓝灰色调
            resources["GlassLightBrush"] = new SolidColorBrush(ColorFromHex("#E6F0F2F8"));  // 浅蓝灰
            resources["CardBrush"] = new SolidColorBrush(ColorFromHex("#F5FFFFFF"));  // 更透明的白色
            resources["CardHoverBrush"] = new SolidColorBrush(ColorFromHex("#FFFAFBFF"));  // 悬停时微蓝
            resources["DeadlineCardBrush"] = new SolidColorBrush(ColorFromHex("#FFFBF5F0"));  // 暖色调

            // 强调色 - 更现代的紫色
            resources["AccentBrush"] = new SolidColorBrush(ColorFromHex("#6366F1"));  // Indigo-500
            resources["AccentLightBrush"] = new SolidColorBrush(ColorFromHex("#818CF8"));  // Indigo-400

            // 状态色 - 更柔和
            resources["SuccessBrush"] = new SolidColorBrush(ColorFromHex("#10B981"));  // Emerald-500
            resources["DangerBrush"] = new SolidColorBrush(ColorFromHex("#EF4444"));  // Red-500
            resources["WarningBrush"] = new SolidColorBrush(ColorFromHex("#F59E0B"));  // Amber-500

            // 文字色 - 更好的对比度
            resources["TextBrush"] = new SolidColorBrush(ColorFromHex("#1E293B"));  // Slate-800
            resources["TextSecondaryBrush"] = new SolidColorBrush(ColorFromHex("#64748B"));  // Slate-500

            // 边框色 - 更细腻
            resources["BorderBrush"] = new SolidColorBrush(ColorFromHex("#1A000000"));  // 更淡的边框
        }

        private void ApplyDarkTheme(ResourceDictionary resources)
        {
            // 背景色 - 深色半透明效果
            resources["GlassBrush"] = new SolidColorBrush(ColorFromHex("#E60F172A"));  // 深蓝黑
            resources["GlassLightBrush"] = new SolidColorBrush(ColorFromHex("#F01E293B"));  // Slate-800
            resources["CardBrush"] = new SolidColorBrush(ColorFromHex("#FF1E293B"));  // Slate-800
            resources["CardHoverBrush"] = new SolidColorBrush(ColorFromHex("#FF334155"));  // Slate-700
            resources["DeadlineCardBrush"] = new SolidColorBrush(ColorFromHex("#FF1A1510"));  // 暖色调深色

            // 强调色 - 深色模式下更亮
            resources["AccentBrush"] = new SolidColorBrush(ColorFromHex("#818CF8"));  // Indigo-400
            resources["AccentLightBrush"] = new SolidColorBrush(ColorFromHex("#A5B4FC"));  // Indigo-300

            // 状态色 - 深色模式下更柔和
            resources["SuccessBrush"] = new SolidColorBrush(ColorFromHex("#34D399"));  // Emerald-400
            resources["DangerBrush"] = new SolidColorBrush(ColorFromHex("#F87171"));  // Red-400
            resources["WarningBrush"] = new SolidColorBrush(ColorFromHex("#FBBF24"));  // Amber-400

            // 文字色 - 深色模式下更清晰
            resources["TextBrush"] = new SolidColorBrush(ColorFromHex("#F1F5F9"));  // Slate-100
            resources["TextSecondaryBrush"] = new SolidColorBrush(ColorFromHex("#94A3B8"));  // Slate-400

            // 边框色 - 深色模式下更细腻
            resources["BorderBrush"] = new SolidColorBrush(ColorFromHex("#1AFFFFFF"));  // 更淡的边框
        }

        private static Color ColorFromHex(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
    }
}
