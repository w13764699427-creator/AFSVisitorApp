using System.ComponentModel.DataAnnotations;

namespace VisitorApp.Models;

/// <summary>
/// 跨多步骤共享的访客填写状态：保存表单输入 + 隐私同意。
/// </summary>
public class VisitorForm
{
    [Required(ErrorMessage = "请输入您的姓名")]
    [StringLength(64, ErrorMessage = "姓名最多 64 个字符")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "请输入手机号")]
    [RegularExpression(@"^1[3-9]\d{9}$", ErrorMessage = "请输入正确的 11 位手机号")]
    public string Phone { get; set; } = string.Empty;

    [StringLength(32)]
    [RegularExpression(@"^$|^[0-9Xx]{15,18}$", ErrorMessage = "身份证号格式不正确")]
    public string IdNumber { get; set; } = string.Empty;

    [StringLength(8)]
    public string Gender { get; set; } = string.Empty;

    [StringLength(16)]
    public string Birthday { get; set; } = string.Empty;

    [StringLength(128)]
    public string Address { get; set; } = string.Empty;

    [StringLength(64)]
    public string IssuingAuthority { get; set; } = string.Empty;

    [StringLength(32)]
    public string IdValidity { get; set; } = string.Empty;

    public string IdCardPhoto { get; set; } = string.Empty;
    public string FacePhoto { get; set; } = string.Empty;

    [StringLength(64)]
    public string Company { get; set; } = string.Empty;

    [Required(ErrorMessage = "请填写被访人")]
    [StringLength(64)]
    public string HostName { get; set; } = string.Empty;

    [StringLength(64)]
    public string HostDepartment { get; set; } = string.Empty;

    /// <summary>选定被访人后由后端档案回填：员工主键 / 工号 / 手机号。</summary>
    public long HostStaffId { get; set; }

    [StringLength(64)]
    public string HostStaffNo { get; set; } = string.Empty;

    [StringLength(32)]
    public string HostMobile { get; set; } = string.Empty;

    [Required(ErrorMessage = "请填写来访事由")]
    [StringLength(256)]
    public string Purpose { get; set; } = string.Empty;

    /// <summary>访问地点（来自门禁分组中 StaffType==1 的项，单选）。</summary>
    public int VisitPlaceId { get; set; }

    [StringLength(128)]
    public string VisitPlaceName { get; set; } = string.Empty;

    /// <summary>访问开始时间，默认当天 0 点。</summary>
    public DateTime VisitStartTime { get; set; } = DateTime.Today;

    /// <summary>访问结束时间，默认当天 23:59:59.999。</summary>
    public DateTime VisitEndTime { get; set; } = DateTime.Today.AddDays(1).AddMilliseconds(-1);

    [Range(0, 20, ErrorMessage = "同行人数应在 0-20 之间")]
    public int Companions { get; set; }

    /// <summary>随行人员明细（姓名 + 手机号）；提交时随主访客一起进入登记单 Visitors 列表。</summary>
    public List<CompanionInfo> CompanionList { get; set; } = new();

    [StringLength(512)]
    public string Notes { get; set; } = string.Empty;

    public bool ConsentAccepted { get; set; }

    public DateTime? ConsentTime { get; set; }

    public void Reset()
    {
        Name = string.Empty;
        Phone = string.Empty;
        IdNumber = string.Empty;
        Gender = string.Empty;
        Birthday = string.Empty;
        Address = string.Empty;
        IssuingAuthority = string.Empty;
        IdValidity = string.Empty;
        IdCardPhoto = string.Empty;
        FacePhoto = string.Empty;
        Company = string.Empty;
        HostName = string.Empty;
        HostDepartment = string.Empty;
        HostStaffId = 0;
        HostStaffNo = string.Empty;
        HostMobile = string.Empty;
        Purpose = string.Empty;
        VisitPlaceId = 0;
        VisitPlaceName = string.Empty;
        VisitStartTime = DateTime.Today;
        VisitEndTime = DateTime.Today.AddDays(1).AddMilliseconds(-1);
        Companions = 0;
        CompanionList = new List<CompanionInfo>();
        Notes = string.Empty;
        ConsentAccepted = false;
        ConsentTime = null;
    }

    public Visitor ToVisitor(string visitCode) => new()
    {
        VisitCode = visitCode,
        Name = Name.Trim(),
        Phone = Phone.Trim(),
        IdNumber = IdNumber?.Trim() ?? string.Empty,
        Gender = Gender?.Trim() ?? string.Empty,
        Birthday = Birthday?.Trim() ?? string.Empty,
        Address = Address?.Trim() ?? string.Empty,
        IssuingAuthority = IssuingAuthority?.Trim() ?? string.Empty,
        IdValidity = IdValidity?.Trim() ?? string.Empty,
        IdCardPhoto = IdCardPhoto ?? string.Empty,
        FacePhoto = FacePhoto ?? string.Empty,
        Company = Company?.Trim() ?? string.Empty,
        HostName = HostName.Trim(),
        HostDepartment = HostDepartment?.Trim() ?? string.Empty,
        Purpose = Purpose.Trim(),
        Companions = CompanionList.Count,
        CheckInTime = DateTime.Now,
        Status = VisitStatus.CheckedIn,
        Notes = Notes?.Trim() ?? string.Empty,
        ConsentAccepted = ConsentAccepted,
        ConsentTime = ConsentTime
    };
}

/// <summary>随行人员：仅需姓名与手机号，提交时映射为登记单 Visitors 明细。</summary>
public class CompanionInfo
{
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
}
