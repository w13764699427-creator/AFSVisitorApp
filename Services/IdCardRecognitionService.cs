using System.Security.Cryptography;

namespace VisitorApp.Services;

/// <summary>
/// 身份证 OCR 结果。Confidence 为整体置信度（0~1）。
/// </summary>
public record IdCardOcrResult(
    string Name,
    string Gender,
    string Ethnicity,
    string Birthday,
    string IdNumber,
    string Address,
    string IssuingAuthority,
    string Validity,
    double Confidence);

public enum IdCardOcrErrorKind
{
    None,
    LowQuality,
    NotAnIdCard,
    NetworkError
}

public record IdCardOcrResponse(
    bool Success,
    IdCardOcrResult? Data,
    IdCardOcrErrorKind ErrorKind,
    string? ErrorMessage)
{
    public static IdCardOcrResponse Ok(IdCardOcrResult data) => new(true, data, IdCardOcrErrorKind.None, null);
    public static IdCardOcrResponse Fail(IdCardOcrErrorKind kind, string message) => new(false, null, kind, message);
}

public interface IIdCardRecognitionService
{
    /// <summary>
    /// 调用 OCR 引擎识别证件正面。返回结构化字段或失败原因。
    /// </summary>
    Task<IdCardOcrResponse> RecognizeAsync(byte[] imageBytes, CancellationToken ct = default);
}

/// <summary>
/// 离线 Mock 实现：
///  · 用 SHA-256 哈希图片字节 → 选取一条预置假身份信息；
///  · 模拟 600~1400ms 网络/算法延迟；
///  · 一定概率回 LowQuality / NotAnIdCard，便于演示失败路径；
///  · 真实接入：替换为 HttpClient 调腾讯云 / 百度 OCR 即可。
/// </summary>
public class MockIdCardRecognitionService : IIdCardRecognitionService
{
    private readonly Random _rand = new();

    private static readonly IdCardOcrResult[] Samples =
    {
        new("张三",   "男", "汉", "1990-05-12", "110101199005120013",
            "北京市东城区景山前街 4 号院 12 号楼 3 单元 201", "北京市公安局东城分局", "2018.06.01-2038.06.01", 0.96),
        new("李四",   "男", "汉", "1985-11-23", "310104198511232415",
            "上海市徐汇区漕溪北路 18 号 35 层 3508",         "上海市公安局徐汇分局", "2020.03.10-2040.03.10", 0.94),
        new("王芳",   "女", "汉", "1993-02-08", "440305199302083024",
            "广东省深圳市南山区科技园南区 8 栋 1502",         "深圳市公安局南山分局", "2017.09.20-长期",         0.95),
        new("陈静",   "女", "汉", "1988-07-19", "330106198807194828",
            "浙江省杭州市西湖区文一西路 998 号",              "杭州市公安局西湖分局", "2019.01.15-2039.01.15", 0.93),
        new("刘洋",   "男", "汉", "1995-09-30", "510104199509303271",
            "四川省成都市锦江区红星路三段 1 号",              "成都市公安局锦江分局", "2021.04.05-2041.04.05", 0.97),
        new("黄磊",   "男", "汉", "1982-12-04", "420106198212043011",
            "湖北省武汉市武昌区中南路 99 号",                  "武汉市公安局武昌分局", "2016.08.18-长期",         0.92),
        new("赵敏",   "女", "汉", "1991-04-17", "320105199104171528",
            "江苏省南京市鼓楼区中山北路 200 号",              "南京市公安局鼓楼分局", "2018.10.01-2038.10.01", 0.95),
        new("周杰",   "男", "汉", "1986-08-08", "350203198608085319",
            "福建省厦门市思明区湖滨南路 88 号",                "厦门市公安局思明分局", "2017.06.06-长期",         0.94),
    };

    public async Task<IdCardOcrResponse> RecognizeAsync(byte[] imageBytes, CancellationToken ct = default)
    {
        await Task.Delay(_rand.Next(600, 1400), ct);

        if (imageBytes is null || imageBytes.Length < 256)
        {
            return IdCardOcrResponse.Fail(IdCardOcrErrorKind.LowQuality,
                "图片过小或为空，请正对证件再拍一次");
        }

        // 8% 概率返回 NotAnIdCard，便于演示失败重试
        if (_rand.NextDouble() < 0.08)
        {
            return IdCardOcrResponse.Fail(IdCardOcrErrorKind.NotAnIdCard,
                "未识别到身份证，请确保证件完整、四角清晰且无反光");
        }

        var hash = SHA256.HashData(imageBytes);
        var idx = Math.Abs(BitConverter.ToInt32(hash, 0)) % Samples.Length;
        return IdCardOcrResponse.Ok(Samples[idx]);
    }
}
