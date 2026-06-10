using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TodoSidebar.Services
{
    /// <summary>
    /// DPAPI 数据保护工具类。
    /// 使用 Windows Data Protection API 加密/解密敏感数据（如 session token）。
    /// 加密后的数据只能在同一用户的同一台机器上解密。
    /// </summary>
    public static class DataProtectionHelper
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TodoSidebar_v1");

        /// <summary>
        /// 加密字符串（返回 Base64 编码的加密数据）
        /// </summary>
        public static string Protect(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return string.Empty;

            try
            {
                var plainBytes = Encoding.UTF8.GetBytes(plainText);
                var encryptedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encryptedBytes);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DataProtection.Protect error: {ex.Message}");
                // DPAPI 不可用时回退到明文（开发环境等）
                return plainText;
            }
        }

        /// <summary>
        /// 解密 Base64 编码的加密数据
        /// </summary>
        public static string Unprotect(string encryptedBase64)
        {
            if (string.IsNullOrEmpty(encryptedBase64))
                return string.Empty;

            try
            {
                var encryptedBytes = Convert.FromBase64String(encryptedBase64);
                var plainBytes = ProtectedData.Unprotect(encryptedBytes, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (FormatException)
            {
                // 不是合法 Base64 — 可能是旧版明文存储，直接返回
                return encryptedBase64;
            }
            catch (CryptographicException)
            {
                // 解密失败（不是 DPAPI 数据，或换了机器/用户）— 返回原文
                System.Diagnostics.Debug.WriteLine("DataProtection.Unprotect failed — returning raw text");
                return encryptedBase64;
            }
        }

        /// <summary>
        /// 判断字符串是否为 DPAPI 加密格式（Base64 且长度合理）
        /// </summary>
        public static bool IsProtected(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length < 20)
                return false;

            try
            {
                Convert.FromBase64String(text);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
