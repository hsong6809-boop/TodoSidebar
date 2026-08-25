using System;
using System.Threading.Tasks;

namespace TodoSidebar.Services
{
    /// <summary>
    /// v5.2 账号中心服务：账号短 ID / 昵称 / 头像的本地缓存 + 云端同步。
    /// 云端权威（account_profile 表），本地 Settings 键缓存，离线降级可用。
    /// </summary>
    public interface IAccountService
    {
        /// <summary>8 位数字短账号 ID；未完成云端供给前为空串。</summary>
        string Uid { get; }

        /// <summary>账号昵称（跨设备同步）；可为空（此时 UI 回退邮箱前缀/默认问候）。</summary>
        string Nickname { get; }

        /// <summary>头像类型："d1"~"d8" 内置 / "custom" 自定义。</summary>
        string AvatarKind { get; }

        /// <summary>是否已完成供给（拿到短 ID）。</summary>
        bool IsProvisioned { get; }

        /// <summary>昵称 / 头像 / UID 任一变化后触发（UI 订阅刷新）。</summary>
        event EventHandler? ProfileChanged;

        /// <summary>
        /// 登录后调用：加载本地缓存 → 拉取云端档案 → 无则建档（分配短 ID、迁移旧昵称）。
        /// 幂等可重入；网络/未建表时静默降级为纯本地模式。fire-and-forget 安全。
        /// </summary>
        Task EnsureProvisionAsync();

        /// <summary>设置账号昵称（本地即时生效 + 尽力上传云端）。</summary>
        Task SetNicknameAsync(string nickname);

        /// <summary>切换为内置头像（"d1"~"d8"，非法值回退 d1）。</summary>
        Task SetBuiltInAvatarAsync(string kind);

        /// <summary>设置自定义头像：读图 → 居中方裁 → 缩至 128px PNG → 本地缓存 + base64 上传。</summary>
        Task SetCustomAvatarAsync(string imageFilePath);

        /// <summary>自定义头像的本地文件路径；kind != custom 或文件缺失时返回 null。</summary>
        string? GetCustomAvatarPath();
    }
}
