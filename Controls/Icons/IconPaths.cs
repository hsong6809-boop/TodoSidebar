using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace TodoSidebar.Controls
{
    /// <summary>单个图标的几何定义。</summary>
    public sealed class IconData
    {
        /// <summary>24×24 网格上的几何图形（已冻结）。</summary>
        public Geometry Geometry { get; }
        /// <summary>true=填充渲染（实心形）；false=描边渲染（线性风格）。</summary>
        public bool Filled { get; }

        internal IconData(string data, bool filled)
        {
            Geometry = Geometry.Parse(data);
            Geometry.Freeze();
            Filled = filled;
        }
    }

    /// <summary>
    /// 矢量图标几何目录（V2-W1）：全部为 24×24 网格手绘 Path，零字体依赖，
    /// 在任何机器上渲染完全一致，彻底解决 Segoe 字体缺字问题。
    /// 风格基准：圆头线性（Material Symbols Outlined 形状语言）。
    /// </summary>
    public static class IconPaths
    {
        private static readonly Dictionary<string, IconData> Catalog =
            new Dictionary<string, IconData>(StringComparer.OrdinalIgnoreCase)
        {
            ["search"] = new(
                "M17 10.5 A6.5 6.5 0 1 0 4 10.5 A6.5 6.5 0 1 0 17 10.5 M15.6 15.6 L20 20", false),

            ["settings"] = new(
                "M4 7 H20 M4 12 H20 M4 17 H20 " +
                "M11 7 A2 2 0 1 0 7 7 A2 2 0 1 0 11 7 " +
                "M17.5 12 A2 2 0 1 0 13.5 12 A2 2 0 1 0 17.5 12 " +
                "M10 17 A2 2 0 1 0 6 17 A2 2 0 1 0 10 17", false),

            ["signout"] = new(
                "M14 4 H7 A1.6 1.6 0 0 0 5.4 5.6 V18.4 A1.6 1.6 0 0 0 7 20 H14 " +
                "M9.5 12 H19.5 M16.5 8.5 L20 12 L16.5 15.5", false),

            ["chevron.left"] = new("M14.5 5.5 L8 12 L14.5 18.5", false),
            ["chevron.right"] = new("M9.5 5.5 L16 12 L9.5 18.5", false),
            ["chevron.up"] = new("M5.5 14.5 L12 8 L18.5 14.5", false),
            ["chevron.down"] = new("M5.5 9.5 L12 16 L18.5 9.5", false),

            ["checkmark"] = new("M5 12.5 L10 17.5 L19 7", false),

            ["delete"] = new(
                "M4.5 7 H19.5 " +
                "M9.5 7 V5.2 A1.2 1.2 0 0 1 10.7 4 H13.3 A1.2 1.2 0 0 1 14.5 5.2 V7 " +
                "M7 7 L7.8 18.6 A1.6 1.6 0 0 0 9.4 20 H14.6 A1.6 1.6 0 0 0 16.2 18.6 L17 7 " +
                "M10.3 10.8 V16.2 M13.7 10.8 V16.2", false),

            ["add"] = new("M12 5 V19 M5 12 H19", false),
            ["close"] = new("M6.5 6.5 L17.5 17.5 M17.5 6.5 L6.5 17.5", false),

            ["upload"] = new("M12 15 V4.5 M8.5 8 L12 4.5 L15.5 8 M5 19.5 H19", false),
            ["download"] = new("M12 4.5 V15 M8.5 11.5 L12 15 L15.5 11.5 M5 19.5 H19", false),

            ["play"] = new("M8 5.2 L19 12 L8 18.8 Z", true),
            ["play.circle"] = new(
                "M20.5 12 A8.5 8.5 0 1 0 3.5 12 A8.5 8.5 0 1 0 20.5 12 " +
                "M10 8.6 L15.4 12 L10 15.4 Z", false),
            ["pause"] = new("M7.5 5 H10.4 V19 H7.5 Z M13.6 5 H16.5 V19 H13.6 Z", true),
            ["stop"] = new("M6.5 6.5 H17.5 V17.5 H6.5 Z", true),

            ["calendar"] = new(
                "M4.5 6.8 H19.5 V19.5 H4.5 Z M4.5 10.8 H19.5 M8.5 4.2 V8.4 M15.5 4.2 V8.4", false),

            ["clock"] = new(
                "M19.5 12 A7.5 7.5 0 1 0 4.5 12 A7.5 7.5 0 1 0 19.5 12 " +
                "M12 7.6 V12 L15.2 13.9", false),

            ["checklist"] = new(
                "M4.5 6 L6 7.5 L9 4.5 M12 6 H19.5 " +
                "M4.5 12 L6 13.5 L9 10.5 M12 12 H19.5 " +
                "M4.5 18 L6 19.5 L9 16.5 M12 18 H19.5", false),

            ["chart"] = new("M4 20 H20 M7.5 20 V11 M12 20 V5.5 M16.5 20 V14", false),
            ["trending"] = new("M3.5 16.5 L9 10.5 L13 14 L20 6.5 M15.5 6.5 H20 V11", false),

            ["star"] = new(
                "M12 3.6 L14.5 8.8 L20.2 9.6 L16.1 13.6 L17.1 19.3 L12 16.6 L6.9 19.3 L7.9 13.6 L3.8 9.6 L9.5 8.8 Z", false),

            ["eye"] = new(
                "M2.8 12 C5.6 6.8 8.9 4.8 12 4.8 C15.1 4.8 18.4 6.8 21.2 12 " +
                "C18.4 17.2 15.1 19.2 12 19.2 C8.9 19.2 5.6 17.2 2.8 12 Z " +
                "M15.2 12 A3.2 3.2 0 1 0 8.8 12 A3.2 3.2 0 1 0 15.2 12", false),

            ["eye.off"] = new(
                "M2.8 12 C5.6 6.8 8.9 4.8 12 4.8 C15.1 4.8 18.4 6.8 21.2 12 " +
                "C18.4 17.2 15.1 19.2 12 19.2 C8.9 19.2 5.6 17.2 2.8 12 Z " +
                "M15.2 12 A3.2 3.2 0 1 0 8.8 12 A3.2 3.2 0 1 0 15.2 12 M5 19 L19 5", false),

            ["lock"] = new(
                "M6.2 10.8 H17.8 V19.6 H6.2 Z " +
                "M8.8 10.8 V7.8 A3.2 3.2 0 0 1 15.2 7.8 V10.8 M12 14 V16.8", false),

            ["info"] = new(
                "M19.5 12 A7.5 7.5 0 1 0 4.5 12 A7.5 7.5 0 1 0 19.5 12 " +
                "M12 11 V16.2 M12 7.6 V8.4", false),

            ["save"] = new(
                "M5.5 4.5 H15.5 L19.5 8.5 V19.5 H5.5 Z " +
                "M8.5 4.5 V9.5 H15 V4.5 M8.5 19.5 V13.5 H15.5 V19.5", false),

            ["sync"] = new(
                "M19.5 12 A7.5 7.5 0 0 0 7.1 6.9 L4.5 9.3 M4.5 4.5 V9.3 H9.3 " +
                "M4.5 12 A7.5 7.5 0 0 0 16.9 17.1 L19.5 14.7 M19.5 19.5 V14.7 H14.7", false),

            ["filter"] = new("M4 6.5 H20 M7.5 12 H16.5 M10.5 17.5 H13.5", false),

            ["droplet"] = new(
                "M12 3.8 C15.4 7.8 17.3 10.6 17.3 13.3 A5.3 5.3 0 1 1 6.7 13.3 C6.7 10.6 8.6 7.8 12 3.8 Z", false),

            ["expand"] = new(
                "M13.5 4.5 H19.5 V10.5 M19.5 4.5 L11.5 12.5 " +
                "M9.5 5.5 H6.5 A1.8 1.8 0 0 0 4.7 7.3 V17.3 A1.8 1.8 0 0 0 6.5 19.1 H16.5 " +
                "A1.8 1.8 0 0 0 18.3 17.3 V14.3", false),

            ["pin"] = new(
                "M14.8 3.8 L20.2 9.2 L16.6 12.8 L16.2 16.2 L7.8 8.2 L11.2 7.4 Z " +
                "M12.4 11.6 L5.5 18.5", false),

            ["timer"] = new(
                "M19.5 13 A7.5 7.5 0 1 0 4.5 13 A7.5 7.5 0 1 0 19.5 13 " +
                "M10 2.8 H14 M12 9.3 V13 L14.6 14.6", false),

            ["mail"] = new("M4 6 H20 V18 H4 Z M4 7.5 L12 13 L20 7.5", false),

            ["more"] = new("M12 5.01 V4.99 M12 12.01 V11.99 M12 19.01 V18.99", false),

            ["restore"] = new(
                "M4.5 9 A8 8 0 1 1 4 13 M4.5 4.5 V9 H9", false),

            ["lightbulb"] = new(
                "M12 3.5 A5.5 5.5 0 0 1 15 13.6 C14.4 14.05 14 14.8 14 15.5 H10 C10 14.8 9.6 14.05 9 13.6 " +
                "A5.5 5.5 0 0 1 12 3.5 Z M10 18.5 H14 M10.8 21 H13.2", false),

            ["sun"] = new(
                "M19.5 12 A7.5 7.5 0 1 0 4.5 12 A7.5 7.5 0 1 0 19.5 12 " +
                "M12 2.2 V4.4 M12 19.6 V21.8 M2.2 12 H4.4 M19.6 12 H21.8 " +
                "M5.1 5.1 L6.6 6.6 M17.4 17.4 L18.9 18.9 M18.9 5.1 L17.4 6.6 M6.6 17.4 L5.1 18.9", false),

            ["moon"] = new(
                "M20.4 13.2 A8.6 8.6 0 1 1 10.8 3.6 A6.8 6.8 0 0 0 20.4 13.2 Z", true),

            // ===== v5.2 内置头像图形（白色实心/线性，置于渐变底上） =====

            ["avatar.star"] = new(
                "M12 2.5 L14.9 8.6 L21.5 9.4 L16.6 13.9 L17.9 20.5 L12 17.3 L6.1 20.5 L7.4 13.9 " +
                "L2.5 9.4 L9.1 8.6 Z", true),

            ["avatar.mountain"] = new(
                "M2.5 19.5 L9 7.5 L13 13.5 L15.5 10 L21.5 19.5 Z", true),

            ["avatar.wave"] = new(
                "M3 9 C6 6.5 9 6.5 12 9 C15 11.5 18 11.5 21 9 " +
                "M3 15 C6 12.5 9 12.5 12 15 C15 17.5 18 17.5 21 15", false),

            ["avatar.tree"] = new(
                "M12 2.8 L17.4 10 H14.6 L19 16 H5 L9.4 10 H6.6 Z M10.7 16 H13.3 V21 H10.7 Z", true),

            ["avatar.flame"] = new(
                "M12 2.8 C15.8 6.6 18.5 9.4 18.5 13.2 A6.5 6.5 0 0 1 5.5 13.2 " +
                "C5.5 10.8 6.7 8.9 8.2 7 C8.9 8.3 9.9 9 10.9 7.4 C11.4 6.4 11.8 4.6 12 2.8 Z", true),

            ["avatar.mooncrescent"] = new(
                "M20.5 14.5 A9 9 0 1 1 9.5 3.5 A7.2 7.2 0 0 0 20.5 14.5 Z", true),

            ["avatar.aurora"] = new(
                "M3 16 C7 12 10 18 14 14 C17 11 19 12 21 10 " +
                "M3 10.5 C7 6.5 10 12.5 14 8.5 C17 5.5 19 6.5 21 4.5", false),

            ["avatar.gem"] = new("M7 4.5 H17 L21.5 10 L12 20.5 L2.5 10 Z", true),

            ["avatar.person"] = new(
                "M12 12 A4 4 0 1 0 12 4 A4 4 0 0 0 12 12 " +
                "M4 21 C4 17 7.5 15 12 15 C16.5 15 20 17 20 21 V21.5 H4 Z", true),
        };

        /// <summary>按名称解析图标（大小写不敏感）；未知名返回 null 并输出调试信息。</summary>
        public static IconData? Resolve(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            if (Catalog.TryGetValue(name.Trim(), out var def)) return def;
            System.Diagnostics.Debug.WriteLine($"IconPaths: 未知图标名称 '{name}'");
            return null;
        }
    }
}
