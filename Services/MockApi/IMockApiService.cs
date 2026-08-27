using VisitorApp.Models;

namespace VisitorApp.Services.MockApi;

/// <summary>
/// 仿真后端 API 的契约。所有方法返回 <see cref="ApiResult{T}"/>，
/// 真实接入时只需更换实现（HttpClient/Refit/gRPC 等），UI 层无须改动。
/// </summary>
public interface IMockApiService
{
    /// <summary>获取首页看板的实时统计。</summary>
    Task<ApiResult<VisitorStats>> GetStatsAsync(CancellationToken ct = default);

    /// <summary>员工目录搜索，用于被访人输入联想。</summary>
    Task<ApiResult<IReadOnlyList<EmployeeInfo>>> SearchEmployeesAsync(string keyword, CancellationToken ct = default);

    /// <summary>提交访客登记，仿真服务端落库 + 通知被访人。</summary>
    Task<ApiResult<VisitorSubmitReceipt>> SubmitVisitorAsync(Visitor visitor, CancellationToken ct = default);

    /// <summary>根据访客编号查询服务端审批状态。</summary>
    Task<ApiResult<string>> QueryApprovalAsync(string visitCode, CancellationToken ct = default);
}
