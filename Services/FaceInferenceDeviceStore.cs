namespace VisitorApp.Services;

/// <summary>
/// 人脸比对推理设备配置（本机持久化）：CPU / GPU / AUTO，默认 CPU。
/// 保存于 MAUI Preferences，下次启动继续沿用。
/// </summary>
public static class FaceInferenceDeviceStore
{
    private const string DeviceKey = "face_inference_device";

    public const string Cpu = "CPU";
    public const string Gpu = "GPU";
    public const string Auto = "AUTO";

    /// <summary>默认推理设备（本机核显多不被新版 OpenVINO GPU 插件支持，CPU 最稳）。</summary>
    public const string Default = Cpu;

    public static string Load()
    {
        try
        {
            var value = Preferences.Get(DeviceKey, Default);
            return Normalize(value);
        }
        catch
        {
            return Default;
        }
    }

    public static void Save(string device)
    {
        try { Preferences.Set(DeviceKey, Normalize(device)); } catch { /* 忽略持久化失败 */ }
    }

    /// <summary>规范化为 CPU / GPU / AUTO，无法识别时回默认。</summary>
    public static string Normalize(string? device) => device?.Trim().ToUpperInvariant() switch
    {
        Cpu => Cpu,
        Gpu => Gpu,
        Auto => Auto,
        _ => Default
    };
}
