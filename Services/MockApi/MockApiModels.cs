namespace VisitorApp.Services.MockApi;

/// <summary>
/// 仿后端接口的返回包装，模仿常见 { code, message, data } 形式。
/// </summary>
public class ApiResult<T>
{
    public int Code { get; set; } = 0;
    public string Message { get; set; } = "ok";
    public T? Data { get; set; }

    public bool IsSuccess => Code == 0;

    public static ApiResult<T> Ok(T data, string message = "ok")
        => new() { Code = 0, Message = message, Data = data };

    public static ApiResult<T> Fail(int code, string message)
        => new() { Code = code, Message = message, Data = default };
}

/// <summary>首页 / 看板的实时指标。</summary>
public record VisitorStats(
    int TodayReceived,
    int CurrentOnSite,
    int AverageSeconds,
    int WeeklyReceived
);

/// <summary>被访人 / 员工目录条目。</summary>
public record EmployeeInfo(
    string Id,
    string Name,
    string Department,
    string Title,
    string Phone,
    string Email
);

/// <summary>调用 SubmitVisitor 后的回执。</summary>
public record VisitorSubmitReceipt(
    string ServerTicketId,
    string ApprovalStatus,
    string HostNotifiedAt
);
