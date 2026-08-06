using System;
using System.Security.Cryptography;
using System.Text;

namespace TodoSidebar.Services
{
    /// <summary>
    /// DPAPI 数据保护工具类。
    /// 使用 Windows Data Protection API 加密/解密敏感数据（如 session token）。
    /// 加密后的数据只能在同一用户的同一台机器上解密。
    /// 加密数据带 "DPAPI:" 前缀；无法识别或解密失败时返回 null，绝不回退明文。
    /// </summary>
    public static class DataProtectionHelper
    {
        /// <summary>密文格式标记，用于区分加密数据与旧版明文数据</summary>
        private const string MagicPrefix = "DPAPI:";

        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TodoSidebar_v1");

        /// <summary>
        /// 加密字符串（返回带前缀的 Base64 密文）。
        /// 加密失败时抛异常，由调用方决定处理（记录日志、不落盘），绝不返回明文。
        /// </summary>
        public static string Protect(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return string.Empty;

            try
            {
                var plainBytes = Encoding.UTF8.GetBytes(plainText);
                var encryptedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
                return MagicPrefix + Convert.ToBase64String(encryptedBytes);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("DPAPI 加密失败，拒绝以明文形式存储敏感数据", ex);
            }
        }

        /// <summary>
        /// 解密字符串。无法识别为密文或解密失败时返回 null（调用方应清除数据并重新登录），不返回原文。
        /// 兼容旧版无前缀的 Base64 密文。
        /// </summary>
        public static string? Unprotect(string data)
        {
            if (string.IsNullOrEmpty(data))
                return null;

            try
            {
                var base64 = data.StartsWith(MagicPrefix, StringComparison.Ordinal)
                    ? data.Substring(MagicPrefix.Length)
                    : data;
                var encryptedBytes = Convert.FromBase64String(base64);
                var plainBytes = ProtectedData.Unprotect(encryptedBytes, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (Exception)
            {
                // 解密失败（换机器/换用户/数据损坏/非密文），无法恢复
                return null;
            }
        }

        /// <summary>
        /// 判断字符串是否为 DPAPI 加密格式（带新前缀，或旧版无前缀但 DPAPI 能成功解密的数据）。
        /// 拒绝纯明文数据。
        /// </summary>
        public static bool IsProtected(string data)
        {
            if (string.IsNullOrEmpty(data))
                return false;

            if (data.StartsWith(MagicPrefix, StringComparison.Ordinal))
                return true;

            // 兼容旧版：无前缀数据必须能通过 DPAPI 解密才算密文，
            // 防止恰好为合法 Base64 的明文被误判为密文
            try
            {
                var encryptedBytes = Convert.FromBase64String(data);
                ProtectedData.Unprotect(encryptedBytes, Entropy, DataProtectionScope.CurrentUser);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
