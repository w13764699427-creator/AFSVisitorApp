using VisitorApp.Models.Kernel;

namespace VisitorApp.Services.Kernel;

/// <summary>
/// 访客登记后端接口契约，按原 WPF 端 <c>VisitorRegistration.Operation</c> 的能力重整为异步形态。
/// 真实实现见 <see cref="KernelVisitorRegistrationApi"/>；离线演示见 <see cref="MockVisitorRegistrationApi"/>。
/// UI 层不直接依赖本接口，而是经 <see cref="VisitorRegistrationService"/> 门面调用。
/// </summary>
public interface IVisitorRegistrationApi
{
    /// <summary>登录换取会话（UserToken）。对应 /KernelService/Login。</summary>
    Task<UserData2?> LoginAsync(string username, string password, CancellationToken ct = default);

    /// <summary>
    /// 提交访客预约 / 登记单。对应 /KernelService/Staff/iVisitorAppointment（原 UpVisitor）。
    /// 实测必须先经本接口建单（预约时间 / 申请人只有它会落库）：直接 VisitorCheckIn 建的单缺这些字段，
    /// 且会被服务端立即自动签退（State 直接到 5）。
    /// </summary>
    Task<WebResultInfo<string>> CreateAppointmentAsync(VisitorRegistrationSheetInfo info, CancellationToken ct = default);

    /// <summary>
    /// 按状态 + 时间段查询登记单列表。对应
    /// /KernelService/VisitorRegistrationSheet/QueryVisitorRegistrationSheet。
    /// </summary>
    Task<List<VisitorRegistrationSheetInfo>> QuerySheetsAsync(string userToken, int state, DateTime startDate, DateTime endDate, CancellationToken ct = default);

    /// <summary>按身份证号查人员档案。对应 /KernelService/Staff/GetStaffByIDCard。</summary>
    Task<StaffInfo?> GetStaffByIdCardAsync(string userToken, string idCard, CancellationToken ct = default);

    /// <summary>按登记单号取登记单。对应 /KernelService/VisitorRegistrationSheet/Get。</summary>
    Task<VisitorRegistrationSheetInfo?> GetSheetAsync(string userToken, long sheetId, CancellationToken ct = default);

    /// <summary>
    /// 按身份证号定位"最近一张"登记单：先查人员，再取其 LeastVisitorRegistrationSheetID。
    /// 复刻原 Operation.GetVisitorRegistrationSheet(token, IDCard, out ...)。
    /// </summary>
    Task<VisitorRegistrationSheetInfo?> GetSheetByIdCardAsync(string userToken, string idCard, CancellationToken ct = default);

    /// <summary>访客签到（入场）。对应 /KernelService/VisitorRegistrationSheet/VisitorCheckIn。</summary>
    Task<Reply> VisitorCheckInAsync(string userToken, VisitorRegistrationSheetInfo info, CancellationToken ct = default);

    /// <summary>访客签退（离场）。对应 /KernelService/VisitorRegistrationSheet/VisitorCheckOut。</summary>
    Task<Reply> VisitorCheckOutAsync(string userToken, VisitorRegistrationSheetInfo info, CancellationToken ct = default);

    /// <summary>来访事由字典。对应 /KernelService/VisitReason/Get。</summary>
    Task<List<VisitReasonInfo>> GetVisitReasonsAsync(string userToken, CancellationToken ct = default);

    /// <summary>全部门禁 / 区域分组。对应 /KernelService/APGroup/GetAllAPGroups（原 GetAllAPGroups，服务端按 StaffType=1 过滤）。</summary>
    Task<AllAPGroupResult?> GetAllAPGroupsAsync(string userToken, CancellationToken ct = default);

    /// <summary>被访人（员工）模糊检索，用于登记时联想。对应 /KernelService/Staff/Search。</summary>
    Task<List<StaffInfo>> SearchStaffAsync(string userToken, StaffParam param, CancellationToken ct = default);
}
