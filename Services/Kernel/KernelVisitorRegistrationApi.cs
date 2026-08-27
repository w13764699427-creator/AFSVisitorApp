using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using VisitorApp.Models.Kernel;

namespace VisitorApp.Services.Kernel;

/// <summary>
/// 真实 /KernelService 后端对接实现。URL 与请求体严格复刻原 WPF 端
/// <c>VisitorRegistration.Operation</c>，并保留其"写入 +8 小时、读取转本地"的时区处理。
/// </summary>
public class KernelVisitorRegistrationApi : IVisitorRegistrationApi
{
    private readonly ApiHelper _api;
    private readonly KernelApiOptions _options;

    public KernelVisitorRegistrationApi(ApiHelper api, KernelApiOptions options)
    {
        _api = api;
        _options = options;
    }

    public async Task<UserData2?> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        var body = new LoginRequest
        {
            UserName = username,
            Password = password,
            ClientID = _options.ClientId,
            KeepTime = _options.KeepTimeSeconds,
        };

        var str = await _api.HttpPostAsync("/KernelService/Login", body, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(str)) return null;

        // 多数环境直接返回 UserData2；个别环境用 WebResultInfo<UserData2> 包装，两者都兼容。
        var direct = TryDeserialize<UserData2>(str);
        if (direct is { IsValid: true }) return direct;

        var wrapped = TryDeserialize<WebResultInfo<UserData2>>(str);
        return wrapped?.data;
    }

    public async Task<WebResultInfo<string>> CreateAppointmentAsync(VisitorRegistrationSheetInfo info, CancellationToken ct = default)
    {
        // 预约提交不做时区平移；先看原始响应再决定解析形状。
        var str = await _api.HttpPostAsync("/KernelService/Staff/iVisitorAppointment", info, ct).ConfigureAwait(false);
        System.Diagnostics.Debug.WriteLine($"[Kernel] iVisitorAppointment 原始响应: {str}");
        return ParseAppointmentReply(str);
    }

    /// <summary>
    /// 按响应实际内容解析提交结果：WebResultInfo 包装 / Reply 风格包装 / 裸登记单号；
    /// 无法识别时把原文片段带在 msg 里，便于现场诊断。
    /// </summary>
    private static WebResultInfo<string> ParseAppointmentReply(string? str)
    {
        if (string.IsNullOrWhiteSpace(str))
            return new WebResultInfo<string> { code = 1, msg = "提交无响应" };

        try
        {
            using var doc = JsonDocument.Parse(str);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                var names = root.EnumerateObject().Select(p => p.Name.ToLowerInvariant()).ToHashSet();

                // Reply 风格：{"code":0,"result":0,"SheetID":123,"msg":"ok"}
                if (names.Contains("sheetid"))
                {
                    var reply = JsonSerializer.Deserialize<Reply>(str, ApiHelper.Json);
                    if (reply is not null)
                        return new WebResultInfo<string>
                        {
                            code = reply.code,
                            result = reply.result,
                            msg = reply.msg,
                            data = reply.SheetID > 0 ? reply.SheetID.ToString() : null,
                        };
                }

                // WebResultInfo 包装：data 可能是字符串，也可能是数字（后端无单号时回 0），逐字段取避免类型不匹配。
                if (names.Contains("code") || names.Contains("result") || names.Contains("msg"))
                {
                    return new WebResultInfo<string>
                    {
                        code = ReadInt(root, "code"),
                        result = ReadInt(root, "result"),
                        msg = ReadValueAsString(root, "msg"),
                        data = ReadValueAsString(root, "data"),
                    };
                }
            }
            else if (root.ValueKind is JsonValueKind.String or JsonValueKind.Number)
            {
                // 裸登记单号（字符串或数字）：按成功处理，data 即单号。
                var v = root.ValueKind == JsonValueKind.Number ? root.GetInt64().ToString() : root.GetString();
                return new WebResultInfo<string> { code = 0, result = 0, data = v };
            }
        }
        catch (JsonException)
        {
            // 非 JSON（如 XML / 纯文本）：不再强解，把原文带回供诊断。
        }

        var snippet = str.Length > 160 ? str[..160] + "…" : str;
        return new WebResultInfo<string> { code = 1, msg = $"提交响应无法解析: {snippet}" };
    }

    /// <summary>大小写不敏感取整型字段（兼容数字与数字字符串）。</summary>
    private static int ReadInt(JsonElement root, string name)
    {
        var el = FindProperty(root, name);
        if (el.ValueKind == JsonValueKind.Number) return el.GetInt32();
        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var i)) return i;
        return 0;
    }

    /// <summary>大小写不敏感取字段并统一转字符串（数字 / 字符串 / 空值都兼容）。</summary>
    private static string? ReadValueAsString(JsonElement root, string name)
    {
        var el = FindProperty(root, name);
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.GetInt64().ToString(),
            _ => null,
        };
    }

    private static JsonElement FindProperty(JsonElement root, string name)
    {
        foreach (var p in root.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value;
        }
        return default;
    }

    public Task<StaffInfo?> GetStaffByIdCardAsync(string userToken, string idCard, CancellationToken ct = default)
    {
        var url = $"/KernelService/Staff/GetStaffByIDCard?UserToken={Esc(userToken)}&IDCard={Esc(idCard)}&StaffTypeList=1";
        return _api.GetAsync<StaffInfo>(url, ct);
    }

    public async Task<VisitorRegistrationSheetInfo?> GetSheetAsync(string userToken, long sheetId, CancellationToken ct = default)
    {
        if (sheetId <= 0) return null;
        var url = $"/KernelService/VisitorRegistrationSheet/Get?UserToken={Esc(userToken)}&SheetID={sheetId}";
        var sheet = await _api.GetAsync<VisitorRegistrationSheetInfo>(url, ct).ConfigureAwait(false);
        if (sheet is not null)
        {
            NormalizeDateTimeToLocal(sheet);
        }
        return sheet;
    }

    public async Task<VisitorRegistrationSheetInfo?> GetSheetByIdCardAsync(string userToken, string idCard, CancellationToken ct = default)
    {
        var staff = await GetStaffByIdCardAsync(userToken, idCard, ct).ConfigureAwait(false);
        if (staff is null || staff.LeastVisitorRegistrationSheetID <= 0) return null;
        return await GetSheetAsync(userToken, staff.LeastVisitorRegistrationSheetID, ct).ConfigureAwait(false);
    }

    public async Task<Reply> VisitorCheckInAsync(string userToken, VisitorRegistrationSheetInfo info, CancellationToken ct = default)
    {
        info.State = 0;
        var url = $"/KernelService/VisitorRegistrationSheet/VisitorCheckIn?UserToken={Esc(userToken)}";
        var payload = _options.ShiftToBeijingTimeOnWrite ? ShiftSheetToBeijing(info) : info;
        var str = await _api.HttpPostAsync(url, payload, ct).ConfigureAwait(false);
        System.Diagnostics.Debug.WriteLine($"[Kernel] VisitorCheckIn 原始响应: {str}");
        return ParseReply(str, info.SheetID, "签到无响应");
    }

    public async Task<Reply> VisitorCheckOutAsync(string userToken, VisitorRegistrationSheetInfo info, CancellationToken ct = default)
    {
        var url = $"/KernelService/VisitorRegistrationSheet/VisitorCheckOut?UserToken={Esc(userToken)}";
        var payload = _options.ShiftToBeijingTimeOnWrite ? ShiftSheetToBeijing(info) : info;
        var str = await _api.HttpPostAsync(url, payload, ct).ConfigureAwait(false);
        System.Diagnostics.Debug.WriteLine($"[Kernel] VisitorCheckOut 原始响应: {str}");
        return ParseReply(str, info.SheetID, "签退无响应");
    }

    /// <summary>按响应实际内容解析签到 / 签退回执：Reply 包装 / 裸单号 / 空对象；无法识别时带回原文片段。</summary>
    private static Reply ParseReply(string? str, long fallbackSheetId, string noResponseMsg)
    {
        if (string.IsNullOrWhiteSpace(str)) return Reply.Fail(-1, noResponseMsg);
        try
        {
            using var doc = JsonDocument.Parse(str);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                var names = root.EnumerateObject().Select(p => p.Name.ToLowerInvariant()).ToHashSet();
                if (names.Contains("code") || names.Contains("result") || names.Contains("sheetid") || names.Contains("msg"))
                {
                    var reply = JsonSerializer.Deserialize<Reply>(str, ApiHelper.Json);
                    if (reply is not null) return reply;
                }
                // 空对象 {} 之类：按成功处理。
                return Reply.Ok(fallbackSheetId);
            }

            if (root.ValueKind == JsonValueKind.Number)
                return Reply.Ok(root.GetInt64() > 0 ? root.GetInt64() : fallbackSheetId);

            if (root.ValueKind == JsonValueKind.String)
            {
                var s = root.GetString() ?? string.Empty;
                if (long.TryParse(s, out var id) && id > 0) return Reply.Ok(id);
                return Reply.Ok(fallbackSheetId, string.IsNullOrEmpty(s) ? "ok" : s);
            }
        }
        catch (JsonException)
        {
            // 非 JSON（如 XML / 纯文本）：带回原文供诊断。
        }

        var snippet = str.Length > 160 ? str[..160] + "…" : str;
        return Reply.Fail(-1, $"响应无法解析: {snippet}");
    }

    public async Task<List<VisitReasonInfo>> GetVisitReasonsAsync(string userToken, CancellationToken ct = default)
    {
        var url = $"/KernelService/VisitReason/Get?UserToken={Esc(userToken)}&VisitReasonID=0";
        var str = await _api.HttpGetAsync(url, ct).ConfigureAwait(false);
        return ParseList<VisitReasonInfo>(str) ?? new List<VisitReasonInfo>();
    }

    public async Task<AllAPGroupResult?> GetAllAPGroupsAsync(string userToken, CancellationToken ct = default)
    {
        // 复刻原 Operation.GetAllAPGroups：服务端按 StaffType=1 过滤访客可访问分组，带分页参数。
        var url = $"/KernelService/APGroup/GetAllAPGroups?UserToken={Esc(userToken)}&StaffType=1&PageIndex=1&PageSize=9999";
        var str = await _api.HttpGetAsync(url, ct).ConfigureAwait(false);

        // 兼容多种返回：带 Groups 的包装对象、裸分组数组；服务端未遵守 Accept 回 XML 时同样兼容。
        var wrapped = TryDeserialize<AllAPGroupResult>(str) ?? TryDeserializeXml<AllAPGroupResult>(str);
        if (wrapped is not null) return wrapped;

        var groups = ParseList<APGroupInfo>(str);
        return groups is { Count: > 0 } ? new AllAPGroupResult { Groups = groups } : null;
    }

    public async Task<List<StaffInfo>> SearchStaffAsync(string userToken, StaffParam param, CancellationToken ct = default)
    {
        // 复刻 Operation.StaffSearchToJson 的查询体（FindFields + 分页参数）。
        // 真实后端对字段匹配类型较敏感，必要时按现场调整 MatchType / FieldType。
        var findFields = new List<FindFieldInfo>();
        if (!string.IsNullOrWhiteSpace(param.StaffName))
            findFields.Add(new FindFieldInfo("StaffName", param.StaffName!, FieldType.FTString, MatchType.Contains));
        if (!string.IsNullOrWhiteSpace(param.StaffNo))
            findFields.Add(new FindFieldInfo("StaffNo", param.StaffNo!, FieldType.FTString, MatchType.Contains));
        if (!string.IsNullOrWhiteSpace(param.Mobile))
            findFields.Add(new FindFieldInfo("Mobile", param.Mobile!, FieldType.FTString, MatchType.Contains));

        var query = new QueryConditionInfo
        {
            FindFields = findFields.ToArray(),
            DisplayFields = "StaffName,StaffID,StaffNo,DepartmentID,DepartmentName,CardID,ResignDate,Mobile",
            SortFields = "StaffName",
            IsAndCondition = true,
        };

        var url = $"/KernelService/Staff/Search?UserToken={Esc(userToken)}" +
                  $"&PageIndex={param.Page}&PageSize={param.Limit}&ResignStatus=0&IncludeMultiCard=false";
        var str = await _api.HttpPostAsync(url, query, ct).ConfigureAwait(false);

        // 兼容三种返回：WebResultInfo 包装、裸数组、现场实际的分页包装（StaffItems）。
        var wrapped = TryDeserialize<WebResultInfo<List<StaffInfo>>>(str);
        if (wrapped?.data is { Count: > 0 }) return wrapped.data;

        var bare = TryDeserialize<List<StaffInfo>>(str);
        if (bare is { Count: > 0 }) return bare;

        var paged = TryDeserialize<StaffSearchResult>(str);
        if (paged?.StaffItems is { Count: > 0 }) return paged.StaffItems;

        return new List<StaffInfo>();
    }

    // ---- 内部辅助 ----

    private static string Esc(string? s) => Uri.EscapeDataString(s ?? string.Empty);

    private static T? TryDeserialize<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;
        try { return JsonSerializer.Deserialize<T>(json, ApiHelper.Json); }
        catch (JsonException) { return default; }
    }

    // ---- XML 兜底解析（服务端未遵守 Accept: application/json、仍回 WCF 默认 XML 时） ----

    private static readonly Regex XmlnsRegex = new(@"\sxmlns(:\w+)?=""[^""]*""", RegexOptions.Compiled);

    /// <summary>列表响应解析：优先 JSON；响应为 ArrayOfXxx 形式的 XML 时用 XmlSerializer 兜底。</summary>
    private static List<T>? ParseList<T>(string? str)
    {
        if (string.IsNullOrWhiteSpace(str)) return null;
        var list = TryDeserialize<List<T>>(str);
        if (list is not null) return list;

        var xmlList = TryDeserializeXml<XmlList<T>>(str,
            rootName: $"ArrayOf{typeof(T).Name}", itemName: typeof(T).Name);
        return xmlList?.Items;
    }

    /// <summary>
    /// XmlSerializer 兜底解析：先剥离默认命名空间再按元素名匹配（后端 DataContract 命名空间因部署而异，只对齐元素名）。
    /// </summary>
    private static T? TryDeserializeXml<T>(string? xml, string? rootName = null, string? itemName = null) where T : class
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;
        if (!xml.TrimStart().StartsWith('<')) return null;
        try
        {
            var plain = XmlnsRegex.Replace(xml, string.Empty);
            XmlSerializer serializer;
            if (itemName is not null)
            {
                var overrides = new XmlAttributeOverrides();
                var attrs = new XmlAttributes();
                attrs.XmlElements.Add(new XmlElementAttribute(itemName));
                overrides.Add(typeof(T), "Items", attrs);
                serializer = rootName is null
                    ? new XmlSerializer(typeof(T), overrides)
                    : new XmlSerializer(typeof(T), overrides, Type.EmptyTypes, new XmlRootAttribute(rootName), null);
            }
            else
            {
                serializer = rootName is null
                    ? new XmlSerializer(typeof(T))
                    : new XmlSerializer(typeof(T), new XmlRootAttribute(rootName));
            }
            using var reader = new StringReader(plain);
            return serializer.Deserialize(reader) as T;
        }
        catch (InvalidOperationException) { return null; }
    }

    /// <summary>XML 数组包装：映射 ArrayOfXxx 根元素下的 Xxx 子元素列表。</summary>
    public sealed class XmlList<T>
    {
        public List<T> Items { get; set; } = new();
    }

    /// <summary>
    /// 复刻原 Operation.VisitorCheckIn 的写入处理：对登记单及其访客的所有时间字段统一 +8 小时。
    /// 通过浅克隆避免污染 UI 侧对象。
    /// </summary>
    private static VisitorRegistrationSheetInfo ShiftSheetToBeijing(VisitorRegistrationSheetInfo src)
    {
        static DateTime Add8(DateTime d) => d == default ? d : d.AddHours(8);

        var clone = src.ShallowClone();
        clone.ApplyDate = Add8(clone.ApplyDate);
        clone.L1ReviewDate = Add8(clone.L1ReviewDate);
        clone.L2ReviewDate = Add8(clone.L2ReviewDate);
        clone.L3ReviewDate = Add8(clone.L3ReviewDate);
        clone.L4ReviewDate = Add8(clone.L4ReviewDate);
        clone.L5ReviewDate = Add8(clone.L5ReviewDate);
        clone.AppointmentEndTime = Add8(clone.AppointmentEndTime);
        clone.AppointmentStartTime = Add8(clone.AppointmentStartTime);
        clone.AppointmentTime = Add8(clone.AppointmentTime);
        clone.CheckInTime = Add8(clone.CheckInTime);
        clone.CheckOutTime = Add8(clone.CheckOutTime);

        if (clone.Visitors is { Count: > 0 })
        {
            clone.Visitors = clone.Visitors.Select(v =>
            {
                var nv = v.ShallowClone();
                nv.CardEffectiveTime = Add8(nv.CardEffectiveTime);
                nv.CardExpiryTime = Add8(nv.CardExpiryTime);
                nv.EntryTime = Add8(nv.EntryTime);
                nv.ExitTime = Add8(nv.ExitTime);
                nv.BirthDate = Add8(nv.BirthDate);
                nv.InDate = Add8(nv.InDate);
                return nv;
            }).ToList();
        }
        return clone;
    }

    /// <summary>读取后把所有 DateTime 字段从 UTC/服务端时区转为本地时区（复刻 Operation.NormalDateTime）。</summary>
    private static void NormalizeDateTimeToLocal(object? obj)
    {
        if (obj is null) return;
        foreach (var p in obj.GetType().GetProperties())
        {
            if (p.PropertyType == typeof(DateTime) && p.CanRead && p.CanWrite)
            {
                var d = (DateTime)p.GetValue(obj)!;
                p.SetValue(obj, d.ToLocalTime());
            }
            else if (p.PropertyType == typeof(List<VisitorInfo>) && p.GetValue(obj) is List<VisitorInfo> vs)
            {
                foreach (var v in vs) NormalizeDateTimeToLocal(v);
            }
        }
    }

    // ---- StaffSearch 查询 DTO（私有，仅供真实后端组装查询体）----
    private enum MatchType { ISNULL, Equal, Contains, GreaterThan, LessThan }
    private enum FieldType { FTString, FTInteger, FTDateTime }

    private sealed class FindFieldInfo
    {
        public FindFieldInfo() { }
        public FindFieldInfo(string fieldId, string fieldValue, FieldType fieldType, MatchType matchType)
        {
            FieldID = fieldId; FieldValue = fieldValue; this.FieldType = fieldType; this.MatchType = matchType;
        }
        public string FieldID { get; set; } = string.Empty;
        public string FieldValue { get; set; } = string.Empty;
        public FieldType FieldType { get; set; }
        public MatchType MatchType { get; set; }
    }

    private sealed class QueryConditionInfo
    {
        public FindFieldInfo[] FindFields { get; set; } = Array.Empty<FindFieldInfo>();
        public string DisplayFields { get; set; } = string.Empty;
        public string SortFields { get; set; } = string.Empty;
        public bool IsAndCondition { get; set; } = true;
    }

    /// <summary>现场 StaffSearch 的分页返回包装：列表在 StaffItems 字段。</summary>
    private sealed class StaffSearchResult
    {
        public int Result { get; set; }
        public int CurrentPageIndex { get; set; }
        public int PageCount { get; set; }
        public int PageSize { get; set; }
        public int RecordCount { get; set; }
        public List<StaffInfo> StaffItems { get; set; } = new();
    }
}
