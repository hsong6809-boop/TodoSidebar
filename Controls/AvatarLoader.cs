using System;
using System.Windows.Media.Imaging;
using TodoSidebar.Services;

namespace TodoSidebar.Controls
{
    /// <summary>
    /// v5.2：把 AccountService 的当前头像状态装载进 AvatarView 的统一入口。
    /// 各窗口订阅 AccountService.ProfileChanged 后调用 Load 刷新即可。
    /// </summary>
    public static class AvatarLoader
    {
        /// <param name="view">目标 AvatarView</param>
        /// <param name="account">账号服务（读取 Kind 与自定义头像路径）</param>
        /// <param name="decodePx">自定义位图解码宽度（≈显示尺寸，省内存）</param>
        /// <param name="fallbackText">首字符兜底文本（昵称/邮箱）</param>
        public static void Load(AvatarView view, AccountService account, double decodePx, string fallbackText)
        {
            var kind = account.AvatarKind ?? "d1";
            if (string.Equals(kind, AvatarCatalog.CustomKind, System.StringComparison.OrdinalIgnoreCase))
            {
                var path = account.GetCustomAvatarPath();
                if (path != null)
                {
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.DecodePixelWidth = (int)Math.Max(16, decodePx);
                        bmp.UriSource = new Uri(path, UriKind.Absolute);
                        bmp.EndInit();
                        bmp.Freeze();
                        view.ImageSource = bmp;
                        view.Kind = kind;
                        view.FallbackText = fallbackText;
                        return;
                    }
                    catch
                    {
                        // 缓存文件损坏：回退内置/首字符渲染，等待下次云同步覆盖
                    }
                }
            }

            view.ImageSource = null!;
            view.Kind = kind;
            view.FallbackText = fallbackText;
        }
    }
}
