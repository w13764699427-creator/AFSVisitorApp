namespace VisitorApp.Models;

/// <summary>
/// 二代身份证读卡器返回的数据。
/// </summary>
public class IdCardReadResult
{
    /// <summary>姓名</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>性别</summary>
    public string Gender { get; set; } = string.Empty;

    /// <summary>身份证号</summary>
    public string IdNumber { get; set; } = string.Empty;

    /// <summary>民族</summary>
    public string EthnicGroup { get; set; } = string.Empty;

    /// <summary>出生日期</summary>
    public string Birthday { get; set; } = string.Empty;

    /// <summary>地址</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>签发机关</summary>
    public string IssuingAuthority { get; set; } = string.Empty;

    /// <summary>有效期开始</summary>
    public string StartDate { get; set; } = string.Empty;

    /// <summary>有效期结束</summary>
    public string EndDate { get; set; } = string.Empty;

    /// <summary>证件照片（JPEG base64 data URL）</summary>
    public string? PhotoBase64 { get; set; }
}
