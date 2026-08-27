using System.Collections.Concurrent;
using VisitorApp.Models.Kernel;

namespace VisitorApp.Services.Kernel;

/// <summary>
/// 全内存的访客登记后端 Mock：让"预约 → 签到 → 查询 → 签退"整套流程在无真实后端时也能完整跑通，
/// 同时严格保持 <see cref="IVisitorRegistrationApi"/> 的返回形状（WebResultInfo / Reply），
/// 接真实后端时仅需在 DI 中替换为 <see cref="KernelVisitorRegistrationApi"/>，UI 无感。
/// </summary>
public class MockVisitorRegistrationApi : IVisitorRegistrationApi
{
    private readonly ConcurrentDictionary<long, VisitorRegistrationSheetInfo> _sheets = new();
    private readonly ConcurrentDictionary<string, long> _latestSheetByIdCard = new();
    private long _seq = 202600000;
    private readonly Random _rand = new();

    private static readonly StaffInfo[] Hosts =
    {
        new() { StaffID = 1001, StaffNo = "E1001", StaffName = "张伟", DepartmentName = "研发部", Mobile = "13800010001" },
        new() { StaffID = 1002, StaffNo = "E1002", StaffName = "王芳", DepartmentName = "研发部", Mobile = "13800010002" },
        new() { StaffID = 1003, StaffNo = "E1003", StaffName = "李娜", DepartmentName = "产品部", Mobile = "13800010003" },
        new() { StaffID = 1004, StaffNo = "E1004", StaffName = "刘洋", DepartmentName = "产品部", Mobile = "13800010004" },
        new() { StaffID = 1005, StaffNo = "E1005", StaffName = "陈静", DepartmentName = "设计部", Mobile = "13800010005" },
        new() { StaffID = 1006, StaffNo = "E1006", StaffName = "杨帆", DepartmentName = "销售部", Mobile = "13800010006" },
        new() { StaffID = 1007, StaffNo = "E1007", StaffName = "黄磊", DepartmentName = "销售部", Mobile = "13800010007" },
        new() { StaffID = 1008, StaffNo = "E1008", StaffName = "赵敏", DepartmentName = "市场部", Mobile = "13800010008" },
        new() { StaffID = 1009, StaffNo = "E1009", StaffName = "周杰", DepartmentName = "财务部", Mobile = "13800010009" },
        new() { StaffID = 1010, StaffNo = "E1010", StaffName = "吴优", DepartmentName = "行政部", Mobile = "13800010010" },
    };

    private static readonly VisitReasonInfo[] Reasons =
    {
        new() { VisitReasonID = 1, VisitReasonName = "商务洽谈", PY = "SWQT" },
        new() { VisitReasonID = 2, VisitReasonName = "技术交流", PY = "JSJL" },
        new() { VisitReasonID = 3, VisitReasonName = "面试", PY = "MS" },
        new() { VisitReasonID = 4, VisitReasonName = "设备维护", PY = "SBWH" },
        new() { VisitReasonID = 5, VisitReasonName = "参观访问", PY = "CGFW" },
        new() { VisitReasonID = 6, VisitReasonName = "送货", PY = "SH" },
        new() { VisitReasonID = 99, VisitReasonName = "其他", PY = "QT" },
    };

    private static readonly APGroupInfo[] APGroups =
    {
        new() { GroupID = 1, GroupName = "一楼大厅", StaffType = 1 },
        new() { GroupID = 2, GroupName = "二楼会议室", StaffType = 1 },
        new() { GroupID = 3, GroupName = "三楼研发区", StaffType = 1 },
        new() { GroupID = 4, GroupName = "五楼行政办公区", StaffType = 1 },
        new() { GroupID = 5, GroupName = "员工通道", StaffType = 2 },
    };

    public async Task<UserData2?> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        await Task.Delay(_rand.Next(80, 200), ct);
        return new UserData2
        {
            UserToken = $"MOCK-{Guid.NewGuid():N}",
            UserID = 1,
            UserName = username,
            RealName = "自助机服务账号",
            ExpireTime = DateTime.Now.AddHours(2),
        };
    }

    public async Task<WebResultInfo<string>> CreateAppointmentAsync(VisitorRegistrationSheetInfo info, CancellationToken ct = default)
    {
        await Task.Delay(_rand.Next(200, 500), ct);

        // 演示失败链路：手机号以 0000 结尾视为黑名单。
        if (!string.IsNullOrEmpty(info.VisitorMobile) && info.VisitorMobile.EndsWith("0000"))
        {
            return new WebResultInfo<string> { code = 0, result = 403, msg = "该手机号已被列入访客黑名单，请联系前台处理" };
        }

        var sheetId = Interlocked.Increment(ref _seq);
        info.SheetID = sheetId;
        if (info.ApplyDate == default) info.ApplyDate = DateTime.Now;
        // 自助登记默认一级审批自动通过，便于直接签到。
        info.L1ReviewState = ReviewState.Agree;
        _sheets[sheetId] = info;

        if (!string.IsNullOrWhiteSpace(info.VisitorIDCard))
            _latestSheetByIdCard[info.VisitorIDCard.Trim()] = sheetId;

        return new WebResultInfo<string> { code = 0, result = 0, msg = "ok", data = sheetId.ToString() };
    }

    public async Task<StaffInfo?> GetStaffByIdCardAsync(string userToken, string idCard, CancellationToken ct = default)
    {
        await Task.Delay(_rand.Next(60, 160), ct);
        if (string.IsNullOrWhiteSpace(idCard)) return null;
        var key = idCard.Trim();
        if (!_latestSheetByIdCard.TryGetValue(key, out var sheetId)) return null;

        var sheet = _sheets.TryGetValue(sheetId, out var s) ? s : null;
        return new StaffInfo
        {
            StaffID = sheetId,
            StaffName = sheet?.VisitorName ?? string.Empty,
            Mobile = sheet?.VisitorMobile ?? string.Empty,
            IDCard = key,
            LeastVisitorRegistrationSheetID = sheetId,
        };
    }

    public async Task<VisitorRegistrationSheetInfo?> GetSheetAsync(string userToken, long sheetId, CancellationToken ct = default)
    {
        await Task.Delay(_rand.Next(60, 160), ct);
        return _sheets.TryGetValue(sheetId, out var s) ? s : null;
    }

    public async Task<VisitorRegistrationSheetInfo?> GetSheetByIdCardAsync(string userToken, string idCard, CancellationToken ct = default)
    {
        var staff = await GetStaffByIdCardAsync(userToken, idCard, ct).ConfigureAwait(false);
        if (staff is null || staff.LeastVisitorRegistrationSheetID <= 0) return null;
        return await GetSheetAsync(userToken, staff.LeastVisitorRegistrationSheetID, ct).ConfigureAwait(false);
    }

    public async Task<Reply> VisitorCheckInAsync(string userToken, VisitorRegistrationSheetInfo info, CancellationToken ct = default)
    {
        await Task.Delay(_rand.Next(150, 350), ct);

        var sheet = info.SheetID > 0 && _sheets.TryGetValue(info.SheetID, out var s) ? s : info;
        sheet.SheetID = info.SheetID > 0 ? info.SheetID : Interlocked.Increment(ref _seq);
        sheet.State = 0;
        sheet.CheckInTime = DateTime.Now;
        if (sheet.Visitors is { Count: > 0 })
        {
            foreach (var v in sheet.Visitors) v.EntryTime = DateTime.Now;
        }
        _sheets[sheet.SheetID] = sheet;
        if (!string.IsNullOrWhiteSpace(sheet.VisitorIDCard))
            _latestSheetByIdCard[sheet.VisitorIDCard.Trim()] = sheet.SheetID;

        return Reply.Ok(sheet.SheetID, "签到成功");
    }

    public async Task<Reply> VisitorCheckOutAsync(string userToken, VisitorRegistrationSheetInfo info, CancellationToken ct = default)
    {
        await Task.Delay(_rand.Next(150, 350), ct);
        if (info.SheetID <= 0 || !_sheets.TryGetValue(info.SheetID, out var sheet))
        {
            return Reply.Fail(404, "未找到对应登记单");
        }
        sheet.State = 1;
        sheet.CheckOutTime = DateTime.Now;
        if (sheet.Visitors is { Count: > 0 })
        {
            foreach (var v in sheet.Visitors) v.ExitTime = DateTime.Now;
        }
        return Reply.Ok(sheet.SheetID, "签退成功");
    }

    public async Task<List<VisitReasonInfo>> GetVisitReasonsAsync(string userToken, CancellationToken ct = default)
    {
        await Task.Delay(_rand.Next(40, 120), ct);
        return Reasons.ToList();
    }

    public async Task<AllAPGroupResult?> GetAllAPGroupsAsync(string userToken, CancellationToken ct = default)
    {
        await Task.Delay(_rand.Next(40, 120), ct);
        return new AllAPGroupResult { Groups = APGroups.ToList() };
    }

    public async Task<List<StaffInfo>> SearchStaffAsync(string userToken, StaffParam param, CancellationToken ct = default)
    {
        await Task.Delay(_rand.Next(60, 180), ct);
        var key = (param.StaffName ?? param.StaffNo ?? param.Mobile ?? string.Empty).Trim();
        if (key.Length == 0) return Hosts.Take(6).ToList();
        return Hosts.Where(h =>
                h.StaffName.Contains(key, StringComparison.OrdinalIgnoreCase) ||
                h.DepartmentName.Contains(key, StringComparison.OrdinalIgnoreCase) ||
                h.StaffNo.Contains(key, StringComparison.OrdinalIgnoreCase) ||
                h.Mobile.Contains(key, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
