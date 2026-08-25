using System;
using System.Windows.Media;

namespace TodoSidebar.Controls
{
    /// <summary>
    /// v5.2 内置头像目录：kind（d1~d8）→ 图形键 + 渐变色对 + 中文名。
    /// 与 AccountService.NormalizeKind 的合法域保持一致（8 枚）。
    /// </summary>
    public static class AvatarCatalog
    {
        public sealed record BuiltIn(string Kind, string GlyphKey, string Name, Color From, Color To);

        public static readonly BuiltIn[] Items =
        {
            new("d1", "avatar.star",         "星辰", FromHex("#6366F1"), FromHex("#A855F7")),
            new("d2", "avatar.mountain",     "山岳", FromHex("#0EA5E9"), FromHex("#2563EB")),
            new("d3", "avatar.wave",         "海浪", FromHex("#06B6D4"), FromHex("#0D9488")),
            new("d4", "avatar.tree",         "森林", FromHex("#34D399"), FromHex("#059669")),
            new("d5", "avatar.flame",        "火焰", FromHex("#F97316"), FromHex("#DC2626")),
            new("d6", "avatar.mooncrescent", "新月", FromHex("#6D28D9"), FromHex("#C026D3")),
            new("d7", "avatar.aurora",       "极光", FromHex("#EC4899"), FromHex("#F43F5E")),
            new("d8", "avatar.gem",          "琥珀", FromHex("#F59E0B"), FromHex("#EAB308")),
        };

        public const string CustomKind = "custom";
        public const string PersonGlyph = "avatar.person";

        public static BuiltIn? Resolve(string? kind)
        {
            var k = (kind ?? "").Trim().ToLowerInvariant();
            foreach (var item in Items)
                if (item.Kind == k) return item;
            return null;
        }

        /// <summary>内置头像的 135° 对角渐变刷。</summary>
        public static LinearGradientBrush Gradient(BuiltIn item)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0),
                EndPoint = new System.Windows.Point(1, 1),
                GradientStops =
                {
                    new GradientStop(item.From, 0),
                    new GradientStop(item.To, 1)
                }
            };
            brush.Freeze();
            return brush;
        }

        private static Color FromHex(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    }
}
