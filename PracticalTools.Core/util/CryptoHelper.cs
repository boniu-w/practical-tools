using System.Security.Cryptography;

namespace PracticalTools.util;

/// <summary>
/// 使用 Windows DPAPI 加密/解密敏感字符串（如 GPG 私钥密码）。
/// 加密后以 Base64 存储，仅限当前 Windows 用户解密。
/// 兼容旧版明文数据：解密失败时返回原文（实现平滑迁移）。
/// </summary>
public static class CryptoHelper
{
    /// <summary>
    /// 加密字符串。空字符串/纯空白字符不加密，直接返回。
    /// </summary>
    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
            return plainText;

        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("旧版密钥口令使用 Windows DPAPI，跨平台口令存储尚未迁移。");

        byte[] plainBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
        // DataProtectionScope.CurrentUser: 只有当前 Windows 用户能解密
        byte[] encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encryptedBytes);
    }

    /// <summary>
    /// 解密字符串。
    /// 如果解密失败（旧版明文数据），直接返回原值。
    /// </summary>
    public static string Decrypt(string cipherText)
    {
        if (string.IsNullOrWhiteSpace(cipherText))
            return cipherText;

        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("旧版密钥口令仅能由原 Windows 用户解密，请使用密钥文件导入功能。");

        try
        {
            byte[] encryptedBytes = Convert.FromBase64String(cipherText);
            byte[] plainBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
            return System.Text.Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            // 解密失败说明是旧版明文数据，直接返回原文
            return cipherText;
        }
    }
}
