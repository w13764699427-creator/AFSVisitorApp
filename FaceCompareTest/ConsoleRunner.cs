using System.Diagnostics;
using System.Text;
using OpenCvSharp;

namespace FaceCompareTest;

/// <summary>无界面比对（命令行模式与界面共用同一套引擎逻辑）。</summary>
public static class ConsoleRunner
{
    public static string ModelsDir => Path.Combine(AppContext.BaseDirectory, "models");

    /// <summary>比对两张图片，返回文字报告。</summary>
    public static string Run(string path1, string path2, string device, float thresholdPercent = 30f)
    {
        var sb = new StringBuilder();
        using var img1 = Cv2.ImRead(path1);
        using var img2 = Cv2.ImRead(path2);
        if (img1.Empty()) return $"无法读取图片：{path1}";
        if (img2.Empty()) return $"无法读取图片：{path2}";

        using var engine = new FaceEngine();
        var sw = Stopwatch.StartNew();
        engine.Init(ModelsDir, device);
        sb.AppendLine($"模型加载完成（{engine.Device}），耗时 {sw.ElapsedMilliseconds}ms");
        if (engine.FallbackNote is not null) sb.AppendLine(engine.FallbackNote);

        sw.Restart();
        var r1 = engine.Process(img1, withQuality: true);
        var r2 = engine.Process(img2, withQuality: true);
        sb.AppendLine($"检测+特征提取耗时 {sw.ElapsedMilliseconds}ms");

        if (r1 is null) { sb.AppendLine("图片1 未检测到人脸"); return sb.ToString(); }
        if (r2 is null) { sb.AppendLine("图片2 未检测到人脸"); return sb.ToString(); }

        var sim = FaceEngine.Cosine(r1.Embedding, r2.Embedding);
        sb.AppendLine($"图片1：人脸框 {r1.Box}，置信度 {r1.Confidence:F3}{QualityText(r1)}");
        sb.AppendLine($"图片2：人脸框 {r2.Box}，置信度 {r2.Confidence:F3}{QualityText(r2)}");
        sb.AppendLine($"相似度：{sim:F4}（{sim:P1}）");
        sb.AppendLine(sim >= thresholdPercent / 100f
            ? $"结论：同一人（通过阈值 {thresholdPercent:F0}%）"
            : $"结论：非同一人（低于阈值 {thresholdPercent:F0}%）");
        return sb.ToString();
    }

    private static string QualityText(FaceResult r)
        => r.Quality is null ? "" : $"，姿态 yaw/pitch/roll = {r.Quality[0]:F2}/{r.Quality[1]:F2}/{r.Quality[2]:F2}";
}
