using SQLite;

namespace VisitorApp.Models;

[Table("Visitors")]
public class Visitor
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [MaxLength(16)]
    public string VisitCode { get; set; } = string.Empty;

    /// <summary>后端登记单号（/KernelService 的 SheetID），用于签到 / 签退贯通真实后端。</summary>
    [Indexed]
    public long SheetId { get; set; }

    [MaxLength(64), NotNull]
    public string Name { get; set; } = string.Empty;

    [MaxLength(32)]
    public string Phone { get; set; } = string.Empty;

    [MaxLength(32)]
    public string IdNumber { get; set; } = string.Empty;

    [MaxLength(8)]
    public string Gender { get; set; } = string.Empty;

    [MaxLength(16)]
    public string Birthday { get; set; } = string.Empty;

    [MaxLength(128)]
    public string Address { get; set; } = string.Empty;

    [MaxLength(64)]
    public string IssuingAuthority { get; set; } = string.Empty;

    [MaxLength(32)]
    public string IdValidity { get; set; } = string.Empty;

    /// <summary>身份证正面照片（Base64，data URL）。</summary>
    public string IdCardPhoto { get; set; } = string.Empty;

    /// <summary>访客本人现场人脸照片（Base64，data URL）。</summary>
    public string FacePhoto { get; set; } = string.Empty;

    [MaxLength(64)]
    public string Company { get; set; } = string.Empty;

    [MaxLength(64)]
    public string HostName { get; set; } = string.Empty;

    [MaxLength(64)]
    public string HostDepartment { get; set; } = string.Empty;

    [MaxLength(256)]
    public string Purpose { get; set; } = string.Empty;

    public int Companions { get; set; }

    public DateTime CheckInTime { get; set; } = DateTime.Now;

    public DateTime? CheckOutTime { get; set; }

    public VisitStatus Status { get; set; } = VisitStatus.CheckedIn;

    [MaxLength(512)]
    public string Notes { get; set; } = string.Empty;

    public bool ConsentAccepted { get; set; }

    public DateTime? ConsentTime { get; set; }

    [Ignore]
    public TimeSpan Duration =>
        (CheckOutTime ?? DateTime.Now) - CheckInTime;

    [Ignore]
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
