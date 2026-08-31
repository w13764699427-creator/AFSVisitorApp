namespace VisitorApp.Models;

/// <summary>
/// 人脸比对结果：现场照与证件照的特征余弦相似度。
/// </summary>
public class FaceCompareResult
{
    /// <summary>是否完成比对（双方均检测到人脸）。</summary>
    public bool Success { get; init; }

    /// <summary>余弦相似度（-1 ~ 1，越大越相似），仅在 Success 时有意义。</summary>
    public float Similarity { get; init; }

    /// <summary>失败原因（未检测到人脸 / 模型缺失等）。</summary>
    public string? Message { get; init; }

    public static FaceCompareResult Ok(float similarity) =>
        new() { Success = true, Similarity = similarity };

    public static FaceCompareResult Fail(string message) =>
        new() { Success = false, Message = message };
}
