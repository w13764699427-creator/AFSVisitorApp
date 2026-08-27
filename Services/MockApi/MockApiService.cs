using VisitorApp.Models;

namespace VisitorApp.Services.MockApi;

/// <summary>
/// 全内存 + 受控延迟的 Mock 后端实现，便于离线演示与单测：
///  · GetStats：基于 <see cref="VisitorDatabase"/> 真实读取的同时叠加少量虚构基数；
///  · SearchEmployees：内置 12 条假员工，按姓名/部门模糊匹配；
///  · SubmitVisitor：随机 200ms~600ms 延迟后回 ServerTicketId；
///  · QueryApproval：根据访客编号末位摆动出 已通过/待审批/已驳回 三态。
/// 真实接入时替换该类即可，UI 层完全无感。
/// </summary>
public class MockApiService : IMockApiService
{
    private readonly VisitorDatabase _db;
    private readonly Random _rand = new();

    private static readonly EmployeeInfo[] Directory =
    {
        new("E1001", "张伟",   "研发部",   "高级工程师",  "13800010001", "zhang.wei@acme.io"),
        new("E1002", "王芳",   "研发部",   "前端工程师",  "13800010002", "wang.fang@acme.io"),
        new("E1003", "李娜",   "产品部",   "产品经理",    "13800010003", "li.na@acme.io"),
        new("E1004", "刘洋",   "产品部",   "高级产品",    "13800010004", "liu.yang@acme.io"),
        new("E1005", "陈静",   "设计部",   "UI 设计师",   "13800010005", "chen.jing@acme.io"),
        new("E1006", "杨帆",   "销售部",   "大客户经理",  "13800010006", "yang.fan@acme.io"),
        new("E1007", "黄磊",   "销售部",   "销售总监",    "13800010007", "huang.lei@acme.io"),
        new("E1008", "赵敏",   "市场部",   "市场经理",    "13800010008", "zhao.min@acme.io"),
        new("E1009", "周杰",   "财务部",   "财务主管",    "13800010009", "zhou.jie@acme.io"),
        new("E1010", "吴优",   "行政部",   "行政专员",    "13800010010", "wu.you@acme.io"),
        new("E1011", "郑伟",   "人事部",   "HRBP",        "13800010011", "zheng.wei@acme.io"),
        new("E1012", "孙琳",   "法务部",   "高级法务",    "13800010012", "sun.lin@acme.io"),
    };

    public MockApiService(VisitorDatabase db)
    {
        _db = db;
    }

    public async Task<ApiResult<VisitorStats>> GetStatsAsync(CancellationToken ct = default)
    {
        await Task.Delay(_rand.Next(120, 280), ct);
        var recent = await _db.GetRecentAsync(200);
        var today = recent.Count(v => v.CheckInTime.Date == DateTime.Today);
        var onSite = recent.Count(v => v.Status == VisitStatus.CheckedIn);
        var weekly = recent.Count(v => v.CheckInTime >= DateTime.Today.AddDays(-6));

        var avg = recent
            .Where(v => v.CheckOutTime.HasValue)
            .Select(v => (int)(v.CheckOutTime!.Value - v.CheckInTime).TotalSeconds)
            .DefaultIfEmpty(28)
            .Average();

        return ApiResult<VisitorStats>.Ok(new VisitorStats(
            TodayReceived: today + 12,
            CurrentOnSite: onSite + 3,
            AverageSeconds: Math.Max(20, (int)avg),
            WeeklyReceived: weekly + 86));
    }

    public async Task<ApiResult<IReadOnlyList<EmployeeInfo>>> SearchEmployeesAsync(string keyword, CancellationToken ct = default)
    {
        await Task.Delay(_rand.Next(80, 220), ct);
        var key = (keyword ?? string.Empty).Trim();
        if (key.Length == 0)
        {
            return ApiResult<IReadOnlyList<EmployeeInfo>>.Ok(Directory.Take(6).ToArray());
        }
        var hits = Directory
            .Where(e => e.Name.Contains(key, StringComparison.OrdinalIgnoreCase)
                     || e.Department.Contains(key, StringComparison.OrdinalIgnoreCase)
                     || e.Title.Contains(key, StringComparison.OrdinalIgnoreCase)
                     || e.Id.Contains(key, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return ApiResult<IReadOnlyList<EmployeeInfo>>.Ok(hits);
    }

    public async Task<ApiResult<VisitorSubmitReceipt>> SubmitVisitorAsync(Visitor visitor, CancellationToken ct = default)
    {
        await Task.Delay(_rand.Next(200, 600), ct);

        // 极少数情况下模拟服务器拒绝（如黑名单），用于演示错误链路
        if (visitor.Phone.EndsWith("0000"))
        {
            return ApiResult<VisitorSubmitReceipt>.Fail(403, "该手机号已被列入访客黑名单，请联系前台处理");
        }

        var ticket = $"SRV-{DateTime.Now:yyyyMMddHHmmss}-{_rand.Next(1000, 9999)}";
        return ApiResult<VisitorSubmitReceipt>.Ok(new VisitorSubmitReceipt(
            ServerTicketId: ticket,
            ApprovalStatus: "auto-approved",
            HostNotifiedAt: DateTime.Now.ToString("HH:mm:ss")));
    }

    public async Task<ApiResult<string>> QueryApprovalAsync(string visitCode, CancellationToken ct = default)
    {
        await Task.Delay(_rand.Next(100, 250), ct);
        if (string.IsNullOrWhiteSpace(visitCode))
        {
            return ApiResult<string>.Fail(400, "访客编号为空");
        }
        var seed = visitCode.Sum(c => c);
        return ApiResult<string>.Ok((seed % 5) switch
        {
            0 => "pending",
            1 => "rejected",
            _ => "approved"
        });
    }
}
