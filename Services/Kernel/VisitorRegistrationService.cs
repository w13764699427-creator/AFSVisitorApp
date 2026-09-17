using VisitorApp.Models;
using VisitorApp.Models.Kernel;

namespace VisitorApp.Services.Kernel;

/// <summary>登记提交结果（含登记单号与签到状态），供 UI 展示与本地落库。</summary>
public record RegistrationResult(bool Success, long SheetId, string Message, bool CheckedIn)
{
    public static RegistrationResult Fail(string message) => new(false, 0, message, false);
}

/// <summary>
/// UI 唯一依赖的业务门面：封装 Token 生命周期、表单↔登记单映射，以及
/// "预约(iVisitorAppointment) → 签到(VisitorCheckIn) → 查询(QueryVisitorRegistrationSheet) → 签退(VisitorCheckOut)" 的编排。
/// 切换真实 / Mock 后端只需在 DI 改 <see cref="IVisitorRegistrationApi"/> 实现，本类与页面均无需改动。
/// </summary>
public class VisitorRegistrationService
{
    private readonly IVisitorRegistrationApi _api;
    private readonly KernelApiOptions _options;
    private readonly SemaphoreSlim _loginLock = new(1, 1);

    private string _token = string.Empty;
    private DateTime _tokenExpiry = DateTime.MinValue;

    public VisitorRegistrationService(IVisitorRegistrationApi api, KernelApiOptions options)
    {
        _api = api;
        _options = options;
    }

    /// <summary>取得（必要时刷新）服务账号会话 Token。登录失败返回空串，由调用方决定降级处理。</summary>
    public async Task<string> EnsureTokenAsync(CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(_token) && DateTime.Now < _tokenExpiry) return _token;

        await _loginLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!string.IsNullOrEmpty(_token) && DateTime.Now < _tokenExpiry) return _token;

            var user = await _api.LoginAsync(_options.ServiceAccount, _options.ServicePassword, ct).ConfigureAwait(false);
            if (user is { IsValid: true })
            {
                _token = user.UserToken;
                // 服务器时钟可能偏差数天（实测曾落后约 5 天）：仅在 ExpireTime 合理（±1 天内）时采信，
                // 否则按本地会话时长估算，避免 token 被提前判定过期或过期仍沿用。
                var localEstimate = DateTime.Now.AddSeconds(Math.Max(60, _options.KeepTimeSeconds - 60));
                _tokenExpiry = user.ExpireTime is { } exp && Math.Abs((exp - DateTime.Now).TotalDays) <= 1
                    ? exp
                    : localEstimate;
            }
            else
            {
                _token = string.Empty;
                _tokenExpiry = DateTime.MinValue;
            }
            return _token;
        }
        finally
        {
            _loginLock.Release();
        }
    }

    /// <summary>
    /// 现场自助登记 = 创建登记单(预约) + 立即签到入场。
    /// 实测：必须先经 iVisitorAppointment 建单（预约时间 / 申请人只有该接口落库），
    /// 直接 VisitorCheckIn 建的单字段不全且会被服务端立即自动签退。
    /// </summary>
    public async Task<RegistrationResult> SubmitRegistrationAsync(VisitorForm form, CancellationToken ct = default)
    {
        var token = await EnsureTokenAsync(ct).ConfigureAwait(false);

        var sheet = BuildSheet(form);

        var create = await _api.CreateAppointmentAsync(sheet, ct).ConfigureAwait(false);
        if (!create.IsSuccess)
        {
            // 重复登记：后端提示访客单已存在时，按身份证查回最近一张单直接签到，
            // 复刻原端 GetVisitorRegistrationSheet(IDCard) + VisitorCheckIn 的处理，避免同一访客被卡死。
            if (IsExistingSheetMsg(create.msg) && !string.IsNullOrWhiteSpace(form.IdNumber))
            {
                var reused = await ReuseExistingSheetAsync(token, form, ct).ConfigureAwait(false);
                if (reused is not null) return reused;
            }
            return RegistrationResult.Fail(string.IsNullOrWhiteSpace(create.msg) ? "登记提交失败" : create.msg!);
        }

        var sheetId = sheet.SheetID;
        if (long.TryParse(create.data, out var parsed) && parsed > 0)
        {
            sheetId = parsed;
        }
        sheet.SheetID = sheetId;

        // 立即签到（失败不阻断登记成功，前台可补签）。
        var checkedIn = false;
        var message = "登记成功";
        try
        {
            var checkin = await _api.VisitorCheckInAsync(token, sheet, ct).ConfigureAwait(false);
            checkedIn = checkin.IsSuccess;
            message = checkedIn ? "登记并签到成功" : (string.IsNullOrWhiteSpace(checkin.msg) ? "登记成功（签到待人工确认）" : checkin.msg!);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // 调用方主动取消，正常上抛
        }
        catch (OperationCanceledException)
        {
            // 请求超时（HttpClient / 每请求超时源触发）：不再吞成"待人工确认"的含糊成功文案。
            message = "登记成功，但签到请求超时，请到前台确认签到状态";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Kernel] 登记后自动签到异常: {ex.Message}");
            message = "登记成功，但签到失败，请到前台人工确认";
        }

        return new RegistrationResult(true, sheetId, message, checkedIn);
    }

    /// <summary>后端提示“访客单已存在”时的回退：查回已有单并尝试签到。</summary>
    private async Task<RegistrationResult?> ReuseExistingSheetAsync(string token, VisitorForm form, CancellationToken ct)
    {
        try
        {
            var existing = await _api.GetSheetByIdCardAsync(token, form.IdNumber!.Trim(), ct).ConfigureAwait(false);
            if (existing is null || existing.SheetID <= 0) return null;

            var checkin = await _api.VisitorCheckInAsync(token, existing, ct).ConfigureAwait(false);
            return new RegistrationResult(
                true,
                existing.SheetID,
                checkin.IsSuccess
                    ? "已存在访客单，签到成功"
                    : $"已存在访客单（单号 {existing.SheetID}），签到：{(string.IsNullOrWhiteSpace(checkin.msg) ? "待人工确认" : checkin.msg)}",
                checkin.IsSuccess);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // 调用方主动取消，正常上抛
        }
        catch (Exception ex)
        {
            // 回退路径失败仅记录，交由上层按"提交失败"原信息处理。
            System.Diagnostics.Debug.WriteLine($"[Kernel] 复用已有访客单签到失败: {ex.Message}");
            return null;
        }
    }

    private static bool IsExistingSheetMsg(string? msg)
        => msg is not null && (msg.Contains("已经存在") || msg.Contains("已存在") || msg.Contains("重复"));

    /// <summary>
    /// 被访人（员工）联想检索：空关键字返回默认员工目录（首页 50 人）；
    /// 有关键字时按“姓名 / 手机号”两路并行模糊匹配后合并去重（后端查询条件为 AND，无法一次 OR）。
    /// </summary>
    public async Task<List<StaffInfo>> SearchHostsAsync(string keyword, CancellationToken ct = default)
    {
        var token = await EnsureTokenAsync(ct).ConfigureAwait(false);
        var key = keyword?.Trim() ?? string.Empty;

        if (key.Length == 0)
            return await _api.SearchStaffAsync(token, new StaffParam { Limit = 50 }, ct).ConfigureAwait(false);

        var byNameTask = _api.SearchStaffAsync(token, new StaffParam { StaffName = key, Limit = 50 }, ct);
        var byMobileTask = _api.SearchStaffAsync(token, new StaffParam { Mobile = key, Limit = 50 }, ct);

        List<StaffInfo> byName = new(), byMobile = new();
        try { byName = await byNameTask.ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Kernel] 被访人按姓名检索失败: {ex.Message}"); /* 单路失败不影响另一路 */ }
        try { byMobile = await byMobileTask.ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Kernel] 被访人按手机号检索失败: {ex.Message}"); }

        return byName
            .Concat(byMobile)
            .GroupBy(s => s.StaffID > 0 ? s.StaffID.ToString() : $"{s.StaffName}|{s.Mobile}")
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>
    /// 选定被访人后，调用 StaffSearch（人员检索）接口按工号 / 姓名精确回查，拿到完整档案
    /// （工号 / 部门 / 手机号等），接口返回后再敲定选择。失败或无命中时回退到联想结果。
    /// </summary>
    public async Task<StaffInfo?> GetHostDetailAsync(StaffInfo selected, CancellationToken ct = default)
    {
        if (selected is null) return null;

        var token = await EnsureTokenAsync(ct).ConfigureAwait(false);

        var param = new StaffParam();
        if (!string.IsNullOrWhiteSpace(selected.StaffNo)) param.StaffNo = selected.StaffNo;
        else if (!string.IsNullOrWhiteSpace(selected.StaffName)) param.StaffName = selected.StaffName;
        else return selected;

        var list = await _api.SearchStaffAsync(token, param, ct).ConfigureAwait(false);
        if (list is not { Count: > 0 }) return selected;

        // 优先按主键命中，其次按工号精确匹配；均未命中且结果唯一时才取首条；
        // 否则回退到用户在联想列表中选定的那一条（Contains 模糊命中多人时不能盲目取首条，避免重名错档）。
        return list.FirstOrDefault(s => selected.StaffID > 0 && s.StaffID == selected.StaffID)
            ?? list.FirstOrDefault(s => !string.IsNullOrWhiteSpace(selected.StaffNo)
                && string.Equals(s.StaffNo, selected.StaffNo, StringComparison.OrdinalIgnoreCase))
            ?? (list.Count == 1 ? list[0] : selected);
    }

    /// <summary>来访事由字典。</summary>
    public async Task<List<VisitReasonInfo>> GetVisitReasonsAsync(CancellationToken ct = default)
    {
        var token = await EnsureTokenAsync(ct).ConfigureAwait(false);
        return await _api.GetVisitReasonsAsync(token, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 访问地点列表：复刻原端 GetAllAPGroups（服务端已按 StaffType=1 过滤）后再映射为 VisitorPlace；
    /// 若响应未携带 StaffType 字段（全为 0）则回退使用全部分组。
    /// </summary>
    public async Task<List<VisitorPlace>> GetVisitPlacesAsync(CancellationToken ct = default)
    {
        var token = await EnsureTokenAsync(ct).ConfigureAwait(false);
        var groups = await _api.GetAllAPGroupsAsync(token, ct).ConfigureAwait(false);
        if (groups?.Groups is not { Count: > 0 }) return new List<VisitorPlace>();

        var visitorGroups = groups.Groups.Where(g => g.StaffType == 1).ToList();
        if (visitorGroups.Count == 0) visitorGroups = groups.Groups;

        return visitorGroups
            .Select(g => new VisitorPlace { VisitorPlaceID = g.GroupID, VisitorPlaceName = g.GroupName })
            .ToList();
    }

    /// <summary>按身份证号查"最近一张"登记单。</summary>
    public async Task<VisitorRegistrationSheetInfo?> QuerySheetByIdCardAsync(string idCard, CancellationToken ct = default)
    {
        var token = await EnsureTokenAsync(ct).ConfigureAwait(false);
        return await _api.GetSheetByIdCardAsync(token, idCard, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 按状态 + 时间段查询登记单（QueryVisitorRegistrationSheet）。
    /// State 实测语义：4=在场、5=已签退、-1=全部。
    /// </summary>
    public async Task<List<VisitorRegistrationSheetInfo>> QuerySheetsAsync(int state, DateTime startDate, DateTime endDate, CancellationToken ct = default)
    {
        var token = await EnsureTokenAsync(ct).ConfigureAwait(false);
        System.Diagnostics.Debug.WriteLine($"[Kernel] 查询登记单（门面）：State={state}, {startDate:yyyy-MM-dd HH:mm} ~ {endDate:yyyy-MM-dd HH:mm}, Token={(string.IsNullOrEmpty(token) ? "⚠ 空（登录未成功）" : "OK")}");
        return await _api.QuerySheetsAsync(token, state, startDate, endDate, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 查询最近登记记录：State=-1 一次查全部状态，按签到时间倒序取前 max 条。
    /// 查询窗口在 days 基础上再前后放宽 10 天：实测服务器时钟可能与本机偏差数天（曾落后约 5 天），
    /// 刚登记的单子可能被盖过去日期的戳，只有宽范围才能稳定命中（窗口下限 40 天，与原行为一致）。
    /// </summary>
    public async Task<List<VisitorRegistrationSheetInfo>> GetRecentSheetsAsync(int days = 30, int max = 30, CancellationToken ct = default)
    {
        var span = Math.Max(days, 40) + 10;
        var start = DateTime.Today.AddDays(-span);
        var end = DateTime.Today.AddDays(span);

        var sheets = await QuerySheetsAsync(-1, start, end, ct).ConfigureAwait(false);

        return sheets
            .Where(s => s.SheetID > 0)
            .GroupBy(s => s.SheetID)
            .Select(g => g.First())
            .OrderByDescending(s => s.CheckInTime == default ? s.ApplyDate : s.CheckInTime)
            .Take(max)
            .ToList();
    }

    /// <summary>查询"在场"（State=4，已签到未签退）的登记单，供签退页检索。窗口同样放宽以容忍服务器时钟偏差。</summary>
    public Task<List<VisitorRegistrationSheetInfo>> GetOnSiteSheetsAsync(int days = 30, CancellationToken ct = default)
    {
        var span = Math.Max(days, 40) + 10;
        return QuerySheetsAsync(4, DateTime.Today.AddDays(-span), DateTime.Today.AddDays(span), ct);
    }

    /// <summary>按登记单号取单。</summary>
    public async Task<VisitorRegistrationSheetInfo?> GetSheetAsync(long sheetId, CancellationToken ct = default)
    {
        var token = await EnsureTokenAsync(ct).ConfigureAwait(false);
        return await _api.GetSheetAsync(token, sheetId, ct).ConfigureAwait(false);
    }

    /// <summary>签退指定登记单。</summary>
    public async Task<Reply> CheckOutAsync(long sheetId, CancellationToken ct = default)
    {
        if (sheetId <= 0) return Reply.Fail(400, "无效的登记单号");
        var token = await EnsureTokenAsync(ct).ConfigureAwait(false);
        var sheet = await _api.GetSheetAsync(token, sheetId, ct).ConfigureAwait(false);
        if (sheet is null) return Reply.Fail(404, "未找到对应登记单");
        sheet.CheckOutTime = DateTime.Now;
        return await _api.VisitorCheckOutAsync(token, sheet, ct).ConfigureAwait(false);
    }

    // ===== 登记单 → 展示模型映射 =====

    /// <summary>
    /// 后端登记单 → 展示用访客模型（仅内存，不落库）：供成功页 / 记录列表 / 签退页复用 VisitorRow。
    /// State 实测语义：4=在场，其余（签退后置 5）视为已离场。
    /// </summary>
    public static Visitor ToVisitor(VisitorRegistrationSheetInfo sheet)
    {
        var main = sheet.Visitors is { Count: > 0 } ? sheet.Visitors[0] : null;

        // 现场照：优先访客明细 PhotoBase64，其次登记单抓拍照片。
        var photo = main?.PhotoBase64;
        var snapshot = sheet.SnapshotPhotos is { Length: > 0 } ? sheet.SnapshotPhotos[0].PhotoData : null;
        string face;
        if (!string.IsNullOrWhiteSpace(photo))
            face = photo.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? photo : $"data:image/jpeg;base64,{photo}";
        else if (snapshot is { Length: > 0 })
            face = $"data:image/jpeg;base64,{Convert.ToBase64String(snapshot)}";
        else
            face = string.Empty;

        return new Visitor
        {
            SheetId = sheet.SheetID,
            VisitCode = sheet.SheetID > 0 ? sheet.SheetID.ToString() : string.Empty,
            Name = sheet.VisitorName,
            Phone = sheet.VisitorMobile,
            IdNumber = sheet.VisitorIDCard,
            Gender = (main?.Gender ?? 0) switch { 1 => "男", 2 => "女", _ => string.Empty },
            Company = sheet.VisitorCompany,
            HostName = sheet.IntervieweeStaffName,
            HostDepartment = sheet.IntervieweeDepartmentName,
            Purpose = sheet.VisitReason,
            Companions = sheet.NumberOfAccompanyingPersons,
            CheckInTime = sheet.CheckInTime == default ? sheet.ApplyDate : sheet.CheckInTime,
            CheckOutTime = sheet.CheckOutTime == default ? null : sheet.CheckOutTime,
            Status = sheet.State == 4 ? VisitStatus.CheckedIn : VisitStatus.CheckedOut,
            FacePhoto = face,
        };
    }

    // ===== 表单 → 登记单映射 =====

    /// <summary>把自助机表单映射为后端登记单（现场即时登记，含一名主访客）。</summary>
    public VisitorRegistrationSheetInfo BuildSheet(VisitorForm form)
    {
        var now = DateTime.Now;

        var visitor = new VisitorInfo
        {
            StaffName = form.Name?.Trim() ?? string.Empty,
            IDCard = form.IdNumber?.Trim() ?? string.Empty,
            Mobile = form.Phone?.Trim() ?? string.Empty,
            Gender = ParseGender(form.Gender),
            Address = form.Address?.Trim() ?? string.Empty,
            IssuingAuthority = form.IssuingAuthority?.Trim() ?? string.Empty,
            BirthDate = ParseDate(form.Birthday),
            EntryTime = now,
            PhotoBase64 = StripDataUrl(form.FacePhoto),
        };

        // Visitors 不可为空：主访客必在其中，随行人员（姓名 + 手机号）逐条追加。
        var visitors = new List<VisitorInfo> { visitor };
        if (form.CompanionList is { Count: > 0 })
        {
            visitors.AddRange(form.CompanionList.Select(c => new VisitorInfo
            {
                StaffName = c.Name?.Trim() ?? string.Empty,
                Mobile = c.Phone?.Trim() ?? string.Empty,
                EntryTime = now,
            }));
        }

        var sheet = new VisitorRegistrationSheetInfo
        {
            IsAppointment = false,
            State = 0,
            VisitorName = visitor.StaffName,
            VisitorIDCard = visitor.IDCard,
            VisitorMobile = visitor.Mobile,
            VisitorCompany = form.Company?.Trim() ?? string.Empty,
            IntervieweeStaffID = form.HostStaffId,
            IntervieweeStaffNo = form.HostStaffNo?.Trim() ?? string.Empty,
            IntervieweeStaffName = form.HostName?.Trim() ?? string.Empty,
            IntervieweeDepartmentName = form.HostDepartment?.Trim() ?? string.Empty,
            IntervieweeMobile = form.HostMobile?.Trim() ?? string.Empty,
            VisitReason = form.Purpose?.Trim() ?? string.Empty,
            VisitPlaceID = form.VisitPlaceId,
            VisitPlaceName = form.VisitPlaceName?.Trim() ?? string.Empty,
            NumberOfAccompanyingPersons = form.CompanionList?.Count ?? 0,
            Applicant = visitor.StaffName,
            ApplyDate = now,
            AppointmentTime = form.VisitStartTime,
            AppointmentStartTime = form.VisitStartTime,
            AppointmentEndTime = form.VisitEndTime,
            CheckInTime = now,
            ConfirmRemark = form.Notes?.Trim() ?? string.Empty,
            Visitors = visitors,
        };

        var faceBytes = TryDecodeBase64(form.FacePhoto);
        if (faceBytes is { Length: > 0 })
        {
            sheet.SnapshotPhotos = new[]
            {
                new VisitorRegistrationSheetSnapshotPhotoInfo { OrderNo = 1, IOType = 0, PhotoData = faceBytes },
            };
        }

        return sheet;
    }

    private static int ParseGender(string? gender) => gender?.Trim() switch
    {
        "男" or "M" or "Male" => 1,
        "女" or "F" or "Female" => 2,
        _ => 0,
    };

    private static DateTime ParseDate(string? text)
        => DateTime.TryParse(text, out var d) ? d : default;

    /// <summary>去掉 data URL 前缀，仅保留纯 base64（供 PhotoBase64 字段）。</summary>
    private static string StripDataUrl(string? dataUrl)
    {
        if (string.IsNullOrWhiteSpace(dataUrl)) return string.Empty;
        var idx = dataUrl.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
        return idx >= 0 ? dataUrl[(idx + "base64,".Length)..] : dataUrl;
    }

    private static byte[]? TryDecodeBase64(string? dataUrl)
    {
        var pure = StripDataUrl(dataUrl);
        if (string.IsNullOrWhiteSpace(pure)) return null;
        try { return Convert.FromBase64String(pure); }
        catch (FormatException) { return null; }
    }
}
