namespace VisitorApp.Services;

using System.Security.Cryptography;
using System.Text;

/// <summary>
/// 应用级偏好设置（本机持久化）：人脸比对相似度通过阈值、系统设置页的管理密码。
/// </summary>
public static class AppSettingsStore
{
    private const string ThresholdKey = "face_similarity_threshold";

    /// <summary>默认相似度阈值（百分比，0~100），与门禁默认值保持一致。</summary>
    public const int DefaultThresholdPercent = 30;

    public static int LoadThresholdPercent()
    {
        try
        {
            var value = Preferences.Get(ThresholdKey, DefaultThresholdPercent);
            return Math.Clamp(value, 0, 100);
        }
        catch
        {
            return DefaultThresholdPercent;
        }
    }

    public static void SaveThresholdPercent(int percent)
    {
        Preferences.Set(ThresholdKey, Math.Clamp(percent, 0, 100));
    }

    // ---- 管理密码（系统设置页解锁）：加盐 SHA-256 存储，DPAPI 保护哈希记录，源码不出现明文 ----

    private const string AdminSaltKey = "settings.admin.salt";
    private const string AdminHashKey = "settings.admin.hash";

    /// <summary>出厂密码的盐（固定）。</summary>
    private const string FactorySalt = "V1s1t0r@App";

    /// <summary>出厂管理密码的加盐 SHA-256（十六进制）。源码只保留哈希，避免反编译直接拿到明文；部署后请在设置页修改。</summary>
    private const string FactoryHash = "0806a2cc9af5d7825342a3012a44673d8c716c11e8342a622b1c9c7c99470a5f";

    /// <summary>验证输入是否与当前管理密码一致（未修改过则比对出厂值）。</summary>
    public static bool VerifyAdminPassword(string? input)
    {
        if (string.IsNullOrEmpty(input)) return false;

        var salt = Preferences.Get(AdminSaltKey, string.Empty);
        var storedHash = PiiProtector.ReadFlexible(Preferences.Get(AdminHashKey, string.Empty));

        if (string.IsNullOrWhiteSpace(salt) || string.IsNullOrWhiteSpace(storedHash))
        {
            // 尚未修改过密码：按出厂凭据比较。
            salt = FactorySalt;
            storedHash = FactoryHash;
        }

        // 存储记录损坏 / 非法十六进制时直接判失败，不抛异常。
        if (storedHash.Length != 64 || storedHash.Any(c => !Uri.IsHexDigit(c))) return false;
        return CryptographicOperations.FixedTimeEquals(Hash(salt, input), Hex(storedHash));
    }

    /// <summary>修改管理密码（随机盐 + DPAPI 保护后立即持久化）。newPassword 为空时恢复出厂密码。</summary>
    public static void ChangeAdminPassword(string? newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword))
        {
            Preferences.Remove(AdminSaltKey);
            Preferences.Remove(AdminHashKey);
            return;
        }

        var salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var hashHex = Convert.ToHexString(Hash(salt, newPassword));
        Preferences.Set(AdminSaltKey, salt);
        Preferences.Set(AdminHashKey, PiiProtector.Protect(hashHex) ?? hashHex);
    }

    private static byte[] Hash(string salt, string password)
        => SHA256.HashData(Encoding.UTF8.GetBytes(salt + password));

    private static byte[] Hex(string hex)
    {
        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }
}
