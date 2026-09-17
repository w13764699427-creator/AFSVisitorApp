using System.Security.Cryptography;
using System.Text;

namespace VisitorApp.Services;

/// <summary>
/// 本地敏感数据保护（Windows DPAPI，当前用户级密钥）：
/// 密文仅在同一 Windows 账户下可解，用于服务密码、身份证号、人脸照片等落盘前的加密。
/// 历史明文数据由 <see cref="ReadFlexible"/> 兼容读取，首次改写后即转为密文。
/// </summary>
public static class PiiProtector
{
    private const string Prefix = "dpapi:";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("VisitorApp.Pii.v1");

    /// <summary>明文 → "dpapi:" + Base64 密文。加密失败（非 Windows / DPAPI 不可用）返回 null，由调用方降级。</summary>
    public static string? Protect(string? plain)
    {
        if (plain is null) return null;
        try
        {
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
            return Prefix + Convert.ToBase64String(bytes);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>解密 "dpapi:" 前缀的密文；非该格式或解密失败返回 false。</summary>
    public static bool TryUnprotect(string? stored, out string plain)
    {
        plain = string.Empty;
        if (string.IsNullOrEmpty(stored) || !stored.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        try
        {
            var bytes = Convert.FromBase64String(stored[Prefix.Length..]);
            plain = Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>兼容读取：密文则解密；否则按明文原样返回（历史数据迁移期兼容）。</summary>
    public static string ReadFlexible(string? stored, string fallback = "")
    {
        if (string.IsNullOrEmpty(stored)) return fallback;
        return TryUnprotect(stored, out var plain) ? plain : stored;
    }
}
