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
/// "预约(iVisitorAppointment) → 签到(VisitorCheckIn) → 查询 → 签退(VisitorCheckOut)" 的编排。
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
                _tokenExpiry = user.ExpireTime ?? DateTime.Now.AddSeconds(Math.Max(60, _options.KeepTimeSeconds - 60));
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
        catch
        {
            message = "登记成功（签到待人工确认）";
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
        catch
        {
            return null;
        }
    }

    private static bool IsExistingSheetMsg(string? msg)
        => msg is not null && (msg.Contains("已经存在") || msg.Contains("已存在"));

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
        try { byName = await byNameTask.ConfigureAwait(false); } catch { /* 单路失败不影响另一路 */ }
        try { byMobile = await byMobileTask.ConfigureAwait(false); } catch { }

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

        // 优先按主键命中，其次按工号，最后取首条。
        return list.FirstOrDefault(s => selected.StaffID > 0 && s.StaffID == selected.StaffID)
            ?? list.FirstOrDefault(s => !string.IsNullOrWhiteSpace(selected.StaffNo)
                && string.Equals(s.StaffNo, selected.StaffNo, StringComparison.OrdinalIgnoreCase))
            ?? list[0];
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
