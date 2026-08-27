namespace VisitorApp.Models.Kernel;

/// <summary>
/// 真实后端（/KernelService）的访客登记单契约。
/// 字段与服务端 JSON 一一对应，便于 <see cref="Services.Kernel.KernelVisitorRegistrationApi"/>
/// 直接序列化提交 / 反序列化读取。自定义字段 F1~F50 原样保留以兼容后端扩展位。
/// </summary>
public class VisitorRegistrationSheetInfo
{
    public long SheetID { get; set; }

    /// <summary>单据状态：0=正常/在场，其余由后端定义。</summary>
    public int State { get; set; }

    /// <summary>是否为预约（true=预约登记，false=现场即时登记）。</summary>
    public bool IsAppointment { get; set; }

    public DateTime AppointmentTime { get; set; }
    public DateTime AppointmentStartTime { get; set; }
    public DateTime AppointmentEndTime { get; set; }

    /// <summary>被访人（员工）信息。</summary>
    public long IntervieweeStaffID { get; set; }
    public string IntervieweeStaffNo { get; set; } = string.Empty;
    public string IntervieweeStaffName { get; set; } = string.Empty;
    public string IntervieweeDepartmentName { get; set; } = string.Empty;
    public string IntervieweeMobile { get; set; } = string.Empty;
    public string IntervieweeIDCard { get; set; } = string.Empty;

    /// <summary>主访客信息。</summary>
    public string VisitorName { get; set; } = string.Empty;
    public string CardID { get; set; } = string.Empty;
    public string VisitorCompany { get; set; } = string.Empty;
    public string VisitorIDCard { get; set; } = string.Empty;
    public string VisitorMobile { get; set; } = string.Empty;
    public string LicensePlate { get; set; } = string.Empty;

    /// <summary>来访事由。</summary>
    public string VisitReason { get; set; } = string.Empty;

    public int VisitPlaceID { get; set; }
    public string VisitPlaceName { get; set; } = string.Empty;

    public DateTime CheckInTime { get; set; }
    public DateTime CheckOutTime { get; set; }

    /// <summary>同行人数。</summary>
    public int NumberOfAccompanyingPersons { get; set; }

    /// <summary>预计停留时长（分钟）。</summary>
    public int StayTime { get; set; }

    public string Applicant { get; set; } = string.Empty;
    public DateTime ApplyDate { get; set; }

    // ===== 5 级审批工作流 =====
    public string L1Reviewer { get; set; } = string.Empty;
    public DateTime L1ReviewDate { get; set; }
    public ReviewState L1ReviewState { get; set; }
    public string L1ReviewRemark { get; set; } = string.Empty;

    public string L2Reviewer { get; set; } = string.Empty;
    public DateTime L2ReviewDate { get; set; }
    public ReviewState L2ReviewState { get; set; }
    public string L2ReviewRemark { get; set; } = string.Empty;

    public string L3Reviewer { get; set; } = string.Empty;
    public DateTime L3ReviewDate { get; set; }
    public ReviewState L3ReviewState { get; set; }
    public string L3ReviewRemark { get; set; } = string.Empty;

    public string L4Reviewer { get; set; } = string.Empty;
    public DateTime L4ReviewDate { get; set; }
    public ReviewState L4ReviewState { get; set; }
    public string L4ReviewRemark { get; set; } = string.Empty;

    public string L5Reviewer { get; set; } = string.Empty;
    public DateTime L5ReviewDate { get; set; }
    public ReviewState L5ReviewState { get; set; }
    public string L5ReviewRemark { get; set; } = string.Empty;

    public VisitorRegistrationSheetCarryGoodsInfo[]? CarryGoods { get; set; }

    /// <summary>登记单内的访客明细（支持一单多访客）。</summary>
    public List<VisitorInfo> Visitors { get; set; } = new();

    public VisitorRegistrationSheetSnapshotPhotoInfo[]? SnapshotPhotos { get; set; }

    public int VehicleID { get; set; }
    public byte ConfirmState { get; set; }
    public string ConfirmRemark { get; set; } = string.Empty;
    public int VisitorTypeID { get; set; }

    // 自定义扩展字段 1~50（兼容后端动态配置位）
    public string? F1 { get; set; }
    public string? F2 { get; set; }
    public string? F3 { get; set; }
    public string? F4 { get; set; }
    public string? F5 { get; set; }
    public string? F6 { get; set; }
    public string? F7 { get; set; }
    public string? F8 { get; set; }
    public string? F9 { get; set; }
    public string? F10 { get; set; }
    public string? F11 { get; set; }
    public string? F12 { get; set; }
    public string? F13 { get; set; }
    public string? F14 { get; set; }
    public string? F15 { get; set; }
    public string? F16 { get; set; }
    public string? F17 { get; set; }
    public string? F18 { get; set; }
    public string? F19 { get; set; }
    public string? F20 { get; set; }
    public string? F21 { get; set; }
    public string? F22 { get; set; }
    public string? F23 { get; set; }
    public string? F24 { get; set; }
    public string? F25 { get; set; }
    public string? F26 { get; set; }
    public string? F27 { get; set; }
    public string? F28 { get; set; }
    public string? F29 { get; set; }
    public string? F30 { get; set; }
    public string? F31 { get; set; }
    public string? F32 { get; set; }
    public string? F33 { get; set; }
    public string? F34 { get; set; }
    public string? F35 { get; set; }
    public string? F36 { get; set; }
    public string? F37 { get; set; }
    public string? F38 { get; set; }
    public string? F39 { get; set; }
    public string? F40 { get; set; }
    public string? F41 { get; set; }
    public string? F42 { get; set; }
    public string? F43 { get; set; }
    public string? F44 { get; set; }
    public string? F45 { get; set; }
    public string? F46 { get; set; }
    public string? F47 { get; set; }
    public string? F48 { get; set; }
    public string? F49 { get; set; }
    public string? F50 { get; set; }

    /// <summary>浅克隆（用于提交前的时区调整，避免污染 UI 侧对象）。</summary>
    public VisitorRegistrationSheetInfo ShallowClone() => (VisitorRegistrationSheetInfo)MemberwiseClone();
}

/// <summary>登记单内的单个访客明细。</summary>
public class VisitorInfo
{
    public long StaffID { get; set; }
    public long VehicleID { get; set; }
    public string VehicleLicensePlate { get; set; } = string.Empty;

    public DateTime EntryTime { get; set; }
    public DateTime ExitTime { get; set; }
    public int StayTime { get; set; }

    public string EntrySnapshotPhotoID { get; set; } = string.Empty;
    public byte[]? EntrySnapshotPhotoData { get; set; }
    public string ExitSnapshotPhotoID { get; set; } = string.Empty;
    public byte[]? ExitSnapshotPhotoData { get; set; }

    /// <summary>工号</summary>
    public string StaffNo { get; set; } = string.Empty;
    /// <summary>名字</summary>
    public string FirstName { get; set; } = string.Empty;
    /// <summary>姓氏</summary>
    public string LastName { get; set; } = string.Empty;
    /// <summary>姓名</summary>
    public string StaffName { get; set; } = string.Empty;
    /// <summary>个人密码</summary>
    public string StaffPin { get; set; } = string.Empty;

    public string CardID { get; set; } = string.Empty;
    public string CardStatus { get; set; } = string.Empty;
    public DateTime CardEffectiveTime { get; set; }
    public DateTime CardExpiryTime { get; set; }

    public string DepartmentID { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;

    public DateTime InDate { get; set; }
    public DateTime BirthDate { get; set; }

    /// <summary>照片</summary>
    public byte[]? Photo { get; set; }
    public string PhotoBase64 { get; set; } = string.Empty;

    /// <summary>性别（0=未知 / 1=男 / 2=女，依后端定义）</summary>
    public int Gender { get; set; } = 0;

    public string IDCard { get; set; } = string.Empty;
    public string IDType { get; set; } = string.Empty;
    public string Nation { get; set; } = string.Empty;
    public string IssuingAuthority { get; set; } = string.Empty;
    public string IDEffectiveTime { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string QQ { get; set; } = string.Empty;
    public string WeChat { get; set; } = string.Empty;
    public string PY { get; set; } = string.Empty;

    public string? F1 { get; set; }
    public string? F2 { get; set; }
    public string? F3 { get; set; }
    public string? F4 { get; set; }
    public string? F5 { get; set; }
    public string? F6 { get; set; }
    public string? F7 { get; set; }
    public string? F8 { get; set; }
    public string? F9 { get; set; }
    public string? F10 { get; set; }
    public string? F11 { get; set; }
    public string? F12 { get; set; }
    public string? F13 { get; set; }
    public string? F14 { get; set; }
    public string? F15 { get; set; }
    public string? F16 { get; set; }
    public string? F17 { get; set; }
    public string? F18 { get; set; }
    public string? F19 { get; set; }
    public string? F20 { get; set; }
    public string? F21 { get; set; }
    public string? F22 { get; set; }
    public string? F23 { get; set; }
    public string? F24 { get; set; }
    public string? F25 { get; set; }
    public string? F26 { get; set; }
    public string? F27 { get; set; }
    public string? F28 { get; set; }
    public string? F29 { get; set; }
    public string? F30 { get; set; }
    public string? F31 { get; set; }
    public string? F32 { get; set; }
    public string? F33 { get; set; }
    public string? F34 { get; set; }
    public string? F35 { get; set; }
    public string? F36 { get; set; }
    public string? F37 { get; set; }
    public string? F38 { get; set; }
    public string? F39 { get; set; }
    public string? F40 { get; set; }
    public string? F41 { get; set; }
    public string? F42 { get; set; }
    public string? F43 { get; set; }
    public string? F44 { get; set; }
    public string? F45 { get; set; }
    public string? F46 { get; set; }
    public string? F47 { get; set; }
    public string? F48 { get; set; }
    public string? F49 { get; set; }
    public string? F50 { get; set; }

    public VehicleInfo? VehicleItem { get; set; }

    /// <summary>浅克隆（用于提交前的时区调整）。</summary>
    public VisitorInfo ShallowClone() => (VisitorInfo)MemberwiseClone();
}

/// <summary>随车 / 车辆信息。</summary>
public class VehicleInfo
{
    public long StaffID { get; set; }
    public DateTime EntryTime { get; set; }
    public DateTime ExitTime { get; set; }
    public int StayTime { get; set; }
    public string EntrySnapshotPhotoID { get; set; } = string.Empty;
    public byte[]? EntrySnapshotPhotoData { get; set; }
    public string ExitSnapshotPhotoID { get; set; } = string.Empty;
    public byte[]? ExitSnapshotPhotoData { get; set; }
    public string LicensePlate { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;

    public string? F1 { get; set; }
    public string? F2 { get; set; }
    public string? F3 { get; set; }
    public string? F4 { get; set; }
    public string? F5 { get; set; }
    public string? F6 { get; set; }
    public string? F7 { get; set; }
    public string? F8 { get; set; }
    public string? F9 { get; set; }
    public string? F10 { get; set; }
    public string? F11 { get; set; }
    public string? F12 { get; set; }
    public string? F13 { get; set; }
    public string? F14 { get; set; }
    public string? F15 { get; set; }
    public string? F16 { get; set; }
    public string? F17 { get; set; }
    public string? F18 { get; set; }
    public string? F19 { get; set; }
    public string? F20 { get; set; }
    public string? F21 { get; set; }
    public string? F22 { get; set; }
    public string? F23 { get; set; }
    public string? F24 { get; set; }
    public string? F25 { get; set; }
    public string? F26 { get; set; }
    public string? F27 { get; set; }
    public string? F28 { get; set; }
    public string? F29 { get; set; }
    public string? F30 { get; set; }
    public string? F31 { get; set; }
    public string? F32 { get; set; }
    public string? F33 { get; set; }
    public string? F34 { get; set; }
    public string? F35 { get; set; }
    public string? F36 { get; set; }
    public string? F37 { get; set; }
    public string? F38 { get; set; }
    public string? F39 { get; set; }
    public string? F40 { get; set; }
    public string? F41 { get; set; }
    public string? F42 { get; set; }
    public string? F43 { get; set; }
    public string? F44 { get; set; }
    public string? F45 { get; set; }
    public string? F46 { get; set; }
    public string? F47 { get; set; }
    public string? F48 { get; set; }
    public string? F49 { get; set; }
    public string? F50 { get; set; }
}

/// <summary>进出抓拍照片。</summary>
public class VisitorRegistrationSheetSnapshotPhotoInfo
{
    public int OrderNo { get; set; }
    /// <summary>0=进场 1=出场（依后端定义）。</summary>
    public int IOType { get; set; }
    public string PhotoID { get; set; } = string.Empty;
    public byte[]? PhotoData { get; set; }
}

/// <summary>随身携带物品。</summary>
public class VisitorRegistrationSheetCarryGoodsInfo
{
    public int IOType { get; set; }
    public string GoodsName { get; set; } = string.Empty;
}

/// <summary>审批状态。</summary>
public enum ReviewState
{
    /// <summary>未审核</summary>
    NotReviewed,
    /// <summary>同意</summary>
    Agree,
    /// <summary>不同意</summary>
    Disagree,
}

/// <summary>来访事由字典项。</summary>
public class VisitReasonInfo
{
    public int VisitReasonID { get; set; }
    public string VisitReasonName { get; set; } = string.Empty;
    public string PY { get; set; } = string.Empty;
}

/// <summary>门禁 / 区域分组项（对应原 Operation.GetAllAPGroups 返回的分组）。</summary>
public class APGroupInfo
{
    public int GroupID { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public int ParentGroupID { get; set; }
    /// <summary>分组适用人员类型：1=访客可访问地点（与原端过滤条件一致）。</summary>
    public int StaffType { get; set; }
}

/// <summary>全部分组查询返回包装（后端以 Groups 数组承载）。</summary>
public class AllAPGroupResult
{
    public List<APGroupInfo> Groups { get; set; } = new();
}

/// <summary>访问地点（由 StaffType==1 的门禁分组映射而来，供登记页单选）。</summary>
public class VisitorPlace
{
    public int VisitorPlaceID { get; set; }
    public string VisitorPlaceName { get; set; } = string.Empty;
}

/// <summary>人员照片。</summary>
public class StaffPhotoInfo
{
    public long StaffID { get; set; }
    public byte[]? Photo { get; set; }
}

/// <summary>键值系统参数。</summary>
public class SystemInfo
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// 服务端统一返回包装。
/// </summary>
/// <typeparam name="T">业务数据类型。</typeparam>
public class WebResultInfo<T>
{
    /// <summary>是否系统异常：0=无异常，1=有系统异常（详见 <see cref="msg"/>）。</summary>
    public int code { get; set; }

    /// <summary>逻辑返回值：0=正常，其它由具体接口定义。</summary>
    public int result { get; set; }

    /// <summary>业务数据。</summary>
    public T? data { get; set; }

    /// <summary>错误信息。</summary>
    public string? msg { get; set; }

    public int pageIndex { get; set; }
    public int pageCount { get; set; }
    public int pageSize { get; set; }
    public int recordCount { get; set; }

    /// <summary>接口调用成功且业务逻辑正常。</summary>
    public bool IsSuccess => code == 0 && result == 0;
}
