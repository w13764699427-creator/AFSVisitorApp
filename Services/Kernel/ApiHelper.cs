using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace VisitorApp.Services.Kernel;

/// <summary>
/// HttpClient 版的 ApiHelper：复刻原 WPF 端 <c>ApiHelper.HttpPost/HttpGet</c> 的职责，
/// 即"拼地址 → 发请求 → 拿原始 JSON 字符串"，再由调用方反序列化。
/// 同时提供泛型便捷方法，统一序列化策略（PascalCase、枚举按数字、忽略 null）。
/// </summary>
public class ApiHelper
{
    private readonly HttpClient _http;
    private readonly KernelApiOptions _options;

    /// <summary>
    /// 与后端约定的 JSON 策略：
    ///  · 属性按 C# 原名（PascalCase / 原样大小写）输出，匹配 KernelService；
    ///  · 读取时大小写不敏感，兼容个别 camelCase 字段（如 WebResultInfo 的 code/msg）；
    ///  · 枚举按数字（与 Newtonsoft 默认一致）。
    /// </summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters =
        {
            // 后端（WCF）的 DateTime 序列化为 "/Date(-220919040000+0800)/"，
            // System.Text.Json 默认不支持，缺失转换器会让整个对象图反序列化失败。
            new WcfDateTimeConverter(),
            new WcfNullableDateTimeConverter(),
        },
    };

    public ApiHelper(HttpClient http, KernelApiOptions options)
    {
        _http = http;
        _options = options;
    }

    /// <summary>POST JSON，返回原始响应字符串。</summary>
    public async Task<string> HttpPostAsync(string url, object? body, CancellationToken ct = default)
    {
        var payload = body is null ? "{}" : JsonSerializer.Serialize(body, body.GetType(), Json);
        var full = BuildUrl(url);
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync(full, content, ct).ConfigureAwait(false);
        Log(full, resp);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    /// <summary>GET，返回原始响应字符串。</summary>
    public async Task<string> HttpGetAsync(string url, CancellationToken ct = default)
    {
        var full = BuildUrl(url);
        using var resp = await _http.GetAsync(full, ct).ConfigureAwait(false);
        Log(full, resp);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    /// <summary>把请求地址与状态码打到调试输出，便于定位 404 到底出在哪个接口路径。</summary>
    private static void Log(string url, HttpResponseMessage resp)
        => System.Diagnostics.Debug.WriteLine($"[Kernel] {(int)resp.StatusCode} {resp.StatusCode} <- {url}");

    /// <summary>
    /// 用当前 <see cref="KernelApiOptions.BaseUrl"/> 把相对路径拼成绝对地址。
    /// 不依赖 HttpClient.BaseAddress（其在首次请求后不可变），从而支持运行时切换服务器 IP。
    /// 传入已是绝对地址时原样返回。
    /// </summary>
    private string BuildUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return _options.BaseUrl;
        if (Uri.TryCreate(url, UriKind.Absolute, out _)) return url;

        var baseUrl = (_options.BaseUrl ?? string.Empty).TrimEnd('/');
        return url.StartsWith('/') ? baseUrl + url : baseUrl + "/" + url;
    }

    /// <summary>POST 并把响应反序列化为 <typeparamref name="T"/>。</summary>
    public async Task<T?> PostAsync<T>(string url, object? body, CancellationToken ct = default)
    {
        var str = await HttpPostAsync(url, body, ct).ConfigureAwait(false);
        return Deserialize<T>(str);
    }

    /// <summary>GET 并把响应反序列化为 <typeparamref name="T"/>。</summary>
    public async Task<T?> GetAsync<T>(string url, CancellationToken ct = default)
    {
        var str = await HttpGetAsync(url, ct).ConfigureAwait(false);
        return Deserialize<T>(str);
    }

    private static T? Deserialize<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;
        try
        {
            return JsonSerializer.Deserialize<T>(json, Json);
        }
        catch (JsonException)
        {
            // 后端个别异常分支可能直接返回纯文本，吞掉解析异常交由上层按 null 处理。
            return default;
        }
    }
}

/// <summary>
/// WCF "/Date(毫秒+时区)/" 格式的 DateTime 转换器：
/// 读入兼容 "/Date(...)/"、ISO 字符串与 Unix 毫秒；写出统一为 WCF 的 "/Date(毫秒±时区)/"
/// （与原 WPF 端 JavaScriptSerializer 输出一致，后端只认这种格式）。
/// </summary>
public sealed class WcfDateTimeConverter : JsonConverter<DateTime>
{
    private static readonly Regex Pattern = new(@"^/Date\((-?\d+)(?:[+-]\d{4})?\)/$", RegexOptions.Compiled);

    /// <summary>服务端对未设置日期的约定（空日期回 1900-01-01 本地时间），提交时空值沿用同一约定。</summary>
    private const string UnsetDate = "/Date(-2209190400000+0800)/";

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => ReadCore(ref reader);

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => writer.WriteStringValue(ToWcfDate(value));

    /// <summary>把 DateTime 转为 "/Date(毫秒±时区)/"；未设置（MinValue）的日期按服务端约定回 1900 年占位。</summary>
    internal static string ToWcfDate(DateTime value)
    {
        if (value == default || value.Year < 1900) return UnsetDate;

        var dto = new DateTimeOffset(value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value);
        var ms = dto.ToUnixTimeMilliseconds();
        var off = dto.Offset;
        var sign = off < TimeSpan.Zero ? "-" : "+";
        return $"/Date({ms}{sign}{off:hhmm})/";
    }

    internal static DateTime ReadCore(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                var s = reader.GetString()!;
                var m = Pattern.Match(s);
                if (m.Success)
                    return DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(m.Groups[1].Value)).LocalDateTime;
                return DateTime.TryParse(s, out var d) ? d : default;
            case JsonTokenType.Number:
                return DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64()).LocalDateTime;
            default:
                return default;
        }
    }
}

/// <summary>可空 DateTime 版的 WCF 日期转换器。</summary>
public sealed class WcfNullableDateTimeConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        return WcfDateTimeConverter.ReadCore(ref reader);
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(WcfDateTimeConverter.ToWcfDate(value.Value));
    }
}
