using SqlSugar;

namespace VisitorApp.Models;

[SugarTable("Visitors")]
public class Visitor
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    [SugarColumn(Length = 16)]
    public string VisitCode { get; set; } = string.Empty;

    /// <summary>后端登记单号（/KernelService 的 SheetID），用于签到 / 签退贯通真实后端。</summary>
    [SugarColumn(IndexGroupNameList = new[] { "idx_visitors_sheet" })]
    public long SheetId { get; set; }

    [SugarColumn(Length = 64, IsNullable = false, IndexGroupNameList = new[] { "idx_visitors_name" })]
    public string Name { get; set; } = string.Empty;

    [SugarColumn(Length = 32, IndexGroupNameList = new[] { "idx_visitors_phone" })]
    public string Phone { get; set; } = string.Empty;

    [SugarColumn(Length = 32)]
    public string IdNumber { get; set; } = string.Empty;

    [SugarColumn(Length = 8)]
    public string Gender { get; set; } = string.Empty;

    [SugarColumn(Length = 16)]
    public string Birthday { get; set; } = string.Empty;

    [SugarColumn(Length = 128)]
    public string Address { get; set; } = string.Empty;

    [SugarColumn(Length = 64)]
    public string IssuingAuthority { get; set; } = string.Empty;

    [SugarColumn(Length = 32)]
    public string IdValidity { get; set; } = string.Empty;

    /// <summary>身份证正面照片（Base64，data URL）。</summary>
    public string IdCardPhoto { get; set; } = string.Empty;

    /// <summary>访客本人现场人脸照片（Base64，data URL）。</summary>
    public string FacePhoto { get; set; } = string.Empty;

    [SugarColumn(Length = 64)]
    public string Company { get; set; } = string.Empty;

    [SugarColumn(Length = 64)]
    public string HostName { get; set; } = string.Empty;

    [SugarColumn(Length = 64)]
    public string HostDepartment { get; set; } = string.Empty;

    [SugarColumn(Length = 256)]
    public string Purpose { get; set; } = string.Empty;

    public int Companions { get; set; }

    public DateTime CheckInTime { get; set; } = DateTime.Now;

    public DateTime? CheckOutTime { get; set; }

    public VisitStatus Status { get; set; } = VisitStatus.CheckedIn;

    [SugarColumn(Length = 512)]
    public string Notes { get; set; } = string.Empty;

    public bool ConsentAccepted { get; set; }

    public DateTime? ConsentTime { get; set; }

    [SugarColumn(IsIgnore = true)]
    public TimeSpan Duration =>
        (CheckOutTime ?? DateTime.Now) - CheckInTime;

    [SugarColumn(IsIgnore = true)]
    public string DurationDisplay
    {
        get
        {
            var d = Duration;
            if (d.TotalMinutes < 1) return "刚刚";
            if (d.TotalHours < 1) return $"{(int)d.TotalMinutes} 分钟";
            if (d.TotalDays < 1) return $"{(int)d.TotalHours} 小时 {d.Minutes} 分";
            return $"{(int)d.TotalDays} 天 {d.Hours} 小时";
        }
    }
}
