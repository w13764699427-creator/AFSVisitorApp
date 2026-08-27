namespace VisitorApp.Models.Kernel;

/// <summary>
/// 登录请求体（对应原 WPF 端的 Login_modle）。
/// </summary>
public class LoginRequest
{
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    /// <summary>客户端标识，后端用于多端会话区分。</summary>
    public string ClientID { get; set; } = string.Empty;
    /// <summary>会话保持时长（秒）。</summary>
    public int KeepTime { get; set; } = 7200;
}

/// <summary>
/// 登录返回的用户会话数据（对应原 UserData2）。核心是 <see cref="UserToken"/>。
/// </summary>
public class UserData2
{
    public string UserToken { get; set; } = string.Empty;
    public long UserID { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string RealName { get; set; } = string.Empty;
    public long StaffID { get; set; }
    public string StaffName { get; set; } = string.Empty;
    public string DepartmentID { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    /// <summary>会话到期时间（部分后端返回，可空）。</summary>
    public DateTime? ExpireTime { get; set; }

    public bool IsValid => !string.IsNullOrWhiteSpace(UserToken);
}

/// <summary>
/// 人员（员工 / 已建档访客）信息。用于被访人检索及按身份证定位最近登记单。
/// </summary>
public class StaffInfo
{
    public long StaffID { get; set; }
    public string StaffNo { get; set; } = string.Empty;
    public string StaffName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string DepartmentID { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string CardID { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;
    public string IDCard { get; set; } = string.Empty;
    public int Gender { get; set; }
    public DateTime? ResignDate { get; set; }
    public byte[]? Photo { get; set; }
    public string PhotoBase64 { get; set; } = string.Empty;

    /// <summary>该人员最近的访客登记单号，用于"按身份证查登记单"。</summary>
    public long LeastVisitorRegistrationSheetID { get; set; }
}

/// <summary>
/// 通用业务回执（对应原 Reply，签到 / 签退接口返回）。
/// </summary>
public class Reply
{
    public int code { get; set; }
    public int result { get; set; }
    public string? msg { get; set; }
    public long SheetID { get; set; }

    public bool IsSuccess => code == 0 && result == 0;

    public static Reply Ok(long sheetId = 0, string? message = "ok")
        => new() { code = 0, result = 0, msg = message, SheetID = sheetId };

    public static Reply Fail(int result, string message)
        => new() { code = 0, result = result == 0 ? -1 : result, msg = message };
}

/// <summary>被访人（员工）检索条件。</summary>
public class StaffParam
{
    public string? StaffName { get; set; }
    public string? StaffNo { get; set; }
    public string? DepartmentName { get; set; }
    public string? Mobile { get; set; }
    public int Page { get; set; } = 1;
    public int Limit { get; set; } = 20;
}
