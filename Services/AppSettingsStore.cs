namespace VisitorApp.Services;

/// <summary>
/// 应用级偏好设置（本机持久化）：目前保存人脸比对相似度通过阈值。
/// </summary>
public static class AppSettingsStore
{
    private const string ThresholdKey = "face_similarity_threshold";

    /// <summary>默认相似度阈值（百分比，0~100），与门禁默认值保持一致。</summary>
    public const int DefaultThresholdPercent = 30;

    public static int LoadThresholdPercent()
    {
        try
        {
            var value = Preferences.Get(ThresholdKey, DefaultThresholdPercent);
            return Math.Clamp(value, 0, 100);
        }
        catch
        {
            return DefaultThresholdPercent;
        }
    }

    public static void SaveThresholdPercent(int percent)
    {
        Preferences.Set(ThresholdKey, Math.Clamp(percent, 0, 100));
    }
}
