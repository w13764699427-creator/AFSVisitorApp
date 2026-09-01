using OpenCvSharp;
using OpenVinoSharp;

namespace FaceCompareTest;

/// <summary>
/// 新模型人脸引擎（OpenVINO IR：detect / extract / quality）。
/// 基于 JYPPX.OpenVINO.CSharp.API，用法与示例 WinOpenVINOFramework.cs 一致：
/// read_model → reshape[NCHW] → compile_model(device) → set_input_tensor → infer → get_output_tensor。
/// </summary>
public sealed class FaceEngine : IDisposable
{
    // ===== 预处理参数（按各模型官方约定，若检测/识别效果异常优先调这里）=====
    private const int DetSize = 640;                 // detect 输入（reshape 固定 640×640）
    private const float DetScale = 1f / 128f;        // SCRFD 标准：scale=1/128, mean=0, RGB
    private const int RecSize = 112;                 // extract 输入
    private const float RecStd = 127.5f;             // ArcFace 标准：(x-127.5)/127.5, RGB
    private const int QuaSize = 224;                 // quality 输入
    private const float DetScoreThreshold = 0.5f;
    private const float NmsIouThreshold = 0.4f;

    private Core? _core;
    private CompiledModel? _detCompiled, _recCompiled, _quaCompiled;
    private InferRequest? _detReq, _recReq, _quaReq;

    public string Device { get; private set; } = string.Empty;

    /// <summary>设备回退说明（如 GPU 不可用自动回落 CPU），无需回退时为 null。</summary>
    public string? FallbackNote { get; private set; }

    /// <summary>初始化三个模型。device: CPU / GPU / AUTO:GPU,CPU；非 CPU 设备不可用时自动回退 CPU。</summary>
    public void Init(string modelsDir, string device)
    {
        try
        {
            InitCore(modelsDir, device);
            Device = device;
        }
        catch (Exception ex) when (device != "CPU")
        {
            FallbackNote = $"设备 {device} 不可用（{FirstLine(ex.Message)}），已自动回退 CPU";
            DisposeCore();
            InitCore(modelsDir, "CPU");
            Device = "CPU";
        }
    }

    private void InitCore(string modelsDir, string device)
    {
        _core = new Core();
        (_detCompiled, _detReq) = Load(modelsDir, "detect", DetSize, DetSize, device);
        (_recCompiled, _recReq) = Load(modelsDir, "extract", RecSize, RecSize, device);
        (_quaCompiled, _quaReq) = Load(modelsDir, "quality", QuaSize, QuaSize, device);
    }

    private static string FirstLine(string s)
    {
        var idx = s.IndexOfAny(new[] { '\r', '\n' });
        return idx < 0 ? s : s[..idx];
    }

    private (CompiledModel compiled, InferRequest request) Load(string modelsDir, string name, int w, int h, string device)
    {
        var xml = Path.Combine(modelsDir, $"{name}.xml");
        var bin = Path.Combine(modelsDir, $"{name}.bin");
        if (!File.Exists(xml) || !File.Exists(bin))
            throw new FileNotFoundException($"缺少模型文件：{name}.xml / {name}.bin（应位于 {modelsDir}）");

        using var model = _core!.read_model(xml, bin);
        // 重新指定输入 Shape [1, 3, H, W]（与示例一致）
        model.reshape(new Shape(new long[] { 1, 3, h, w }));
        var compiled = _core.compile_model(model, device);
        var request = compiled.create_infer_request();
        return (compiled, request);
    }

    /// <summary>对一张图检测人脸并提取特征，返回置信度最高的一张脸；无人脸返回 null。</summary>
    public FaceResult? Process(Mat img, bool withQuality)
    {
        // ===== 1. 检测 =====
        using var detMat = PadToSize(img, DetSize, DetSize);
        Infer(_detReq!, MatToNchw(detMat, DetScale, 0f), 1, 3, DetSize, DetSize);

        var strides = new[] { 8, 16, 32 };
        var boxes = new List<DetBox>();
        for (var idx = 0; idx < strides.Length; idx++)
        {
            var stride = strides[idx];
            // 9 个输出：0~2 分数（stride 8/16/32），3~5 框，6~8 五点关键点
            var scores = GetOutput(_detReq!, idx);
            var bboxes = GetOutput(_detReq!, idx + 3);
            var lmks = GetOutput(_detReq!, idx + 6);
            var blockSize = (int)Math.Sqrt(scores.Length / 2.0);

            for (var i = 0; i < scores.Length; i++)
            {
                if (scores[i] < DetScoreThreshold) continue;
                var col = (i / 2) % blockSize;
                var row = (i / 2) / blockSize;
                var cx = col * stride;
                var cy = row * stride;

                boxes.Add(new DetBox
                {
                    X = cx - bboxes[i * 4] * stride,
                    Y = cy - bboxes[i * 4 + 1] * stride,
                    Width = (bboxes[i * 4] + bboxes[i * 4 + 2]) * stride,
                    Height = (bboxes[i * 4 + 1] + bboxes[i * 4 + 3]) * stride,
                    Confidence = scores[i],
                    Landmarks = Enumerable.Range(0, 5).Select(p => new Point2f(
                        cx + lmks[i * 10 + p * 2] * stride,
                        cy + lmks[i * 10 + p * 2 + 1] * stride)).ToArray()
                });
            }
        }

        if (boxes.Count == 0) return null;
        var best = Nms(boxes, NmsIouThreshold)[0];

        // ===== 2. ArcFace 五点对齐 → 112×112 → 特征提取 =====
        using var aligned = AlignFace(detMat, best.Landmarks, RecSize);
        Infer(_recReq!, MatToNchw(aligned, 1f / RecStd, -1f), 1, 3, RecSize, RecSize);
        var embedding = GetOutput(_recReq!, 0);

        // ===== 3. 姿态质量（yaw/pitch/roll）：外扩人脸区域 → 224×224 =====
        float[]? quality = null;
        if (withQuality)
        {
            using var crop = CropExpanded(detMat, best.X, best.Y, best.Width, best.Height, 1.5f);
            using var resized = new Mat();
            Cv2.Resize(crop, resized, new OpenCvSharp.Size(QuaSize, QuaSize));
            Infer(_quaReq!, MatToNchw(resized, 1f / 255f, 0f), 1, 3, QuaSize, QuaSize);
            quality = new[] { GetOutput(_quaReq!, 0)[0], GetOutput(_quaReq!, 1)[0], GetOutput(_quaReq!, 2)[0] };
        }

        return new FaceResult
        {
            Box = new Rect((int)best.X, (int)best.Y, (int)best.Width, (int)best.Height),
            Confidence = best.Confidence,
            Landmarks = best.Landmarks,
            Embedding = embedding,
            Quality = quality
        };
    }

    /// <summary>余弦相似度。</summary>
    public static float Cosine(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return (float)(dot / (Math.Sqrt(na) * Math.Sqrt(nb) + 1e-9));
    }

    // ===== 内部工具 =====

    /// <summary>构造 [n,3,h,w] 输入张量并推理。</summary>
    private static void Infer(InferRequest request, float[] input, int n, int c, int h, int w)
    {
        using var tensor = new Tensor(new Shape(new long[] { n, c, h, w }), input);
        request.set_input_tensor(tensor);
        request.infer();
    }

    /// <summary>读取第 index 个输出张量为 float 数组（张量归请求所有，不释放）。</summary>
    private static float[] GetOutput(InferRequest request, int index)
    {
        var tensor = request.get_output_tensor((ulong)index);
        return tensor.get_span<float>().ToArray();
    }

    /// <summary>BGR Mat → RGB NCHW float 数组：dst = src * scale + shift。</summary>
    private static float[] MatToNchw(Mat mat, float scale, float shift)
    {
        using var rgb = new Mat();
        Cv2.CvtColor(mat, rgb, ColorConversionCodes.BGR2RGB);
        using var f = new Mat();
        rgb.ConvertTo(f, MatType.CV_32FC3, scale, shift);

        Cv2.Split(f, out var channels);
        try
        {
            var plane = mat.Width * mat.Height;
            var data = new float[3 * plane];
            for (var c = 0; c < 3; c++)
                System.Runtime.InteropServices.Marshal.Copy(channels[c].Data, data, c * plane, plane);
            return data;
        }
        finally
        {
            foreach (var ch in channels) ch?.Dispose();
        }
    }

    /// <summary>等比缩放后居中补黑边。</summary>
    private static Mat PadToSize(Mat src, int w, int h)
    {
        var rate = Math.Min(w * 1.0 / src.Width, h * 1.0 / src.Height);
        Mat working = src;
        Mat? resized = null;
        if (rate < 1.0) { resized = src.Resize(new OpenCvSharp.Size(0, 0), rate, rate); working = resized; }
        try
        {
            var dst = new Mat();
            var pw = w - working.Width; var ph = h - working.Height;
            Cv2.CopyMakeBorder(working, dst, ph / 2, ph - ph / 2, pw / 2, pw - pw / 2, BorderTypes.Constant, new Scalar(0, 0, 0));
            return dst;
        }
        finally { resized?.Dispose(); }
    }

    private static Mat CropExpanded(Mat src, float x, float y, float w, float h, float expand)
    {
        var cx = x + w / 2; var cy = y + h / 2;
        var side = Math.Max(w, h) * expand / 2;
        var x1 = (int)Math.Max(0, cx - side); var y1 = (int)Math.Max(0, cy - side);
        var x2 = (int)Math.Min(src.Width, cx + side); var y2 = (int)Math.Min(src.Height, cy + side);
        return new Mat(src, new Rect(x1, y1, Math.Max(1, x2 - x1), Math.Max(1, y2 - y1))).Clone();
    }

    private static Mat AlignFace(Mat img, Point2f[] landmarks, int size)
    {
        var arcfaceStd = new[]
        {
            new Point2f(38.2946f, 51.6963f), new Point2f(73.5318f, 51.5014f),
            new Point2f(56.0252f, 71.7366f), new Point2f(41.5493f, 92.3655f),
            new Point2f(70.7299f, 92.2041f)
        };
        using var m = Cv2.EstimateAffinePartial2D(InputArray.Create(landmarks), InputArray.Create(arcfaceStd));
        var aligned = new Mat();
        if (m is null) { Cv2.Resize(img, aligned, new OpenCvSharp.Size(size, size)); return aligned; }
        Cv2.WarpAffine(img, aligned, m, new OpenCvSharp.Size(size, size));
        return aligned;
    }

    private static List<DetBox> Nms(List<DetBox> boxes, float iouThreshold)
    {
        var sorted = boxes.OrderByDescending(b => b.Confidence).ToList();
        var selected = new List<DetBox>();
        while (sorted.Count > 0)
        {
            var cur = sorted[0];
            selected.Add(cur);
            sorted.RemoveAt(0);
            sorted = sorted.Where(b => IoU(cur, b) <= iouThreshold).ToList();
        }
        return selected;
    }

    private static float IoU(DetBox a, DetBox b)
    {
        var x1 = Math.Max(a.X, b.X); var y1 = Math.Max(a.Y, b.Y);
        var x2 = Math.Min(a.X + a.Width, b.X + b.Width); var y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);
        var inter = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
        var union = a.Width * a.Height + b.Width * b.Height - inter;
        return union > 0 ? inter / union : 0;
    }

    public void Dispose()
    {
        DisposeCore();
    }

    private void DisposeCore()
    {
        foreach (var r in new[] { _detReq, _recReq, _quaReq }) r?.Dispose();
        foreach (var c in new[] { _detCompiled, _recCompiled, _quaCompiled }) c?.Dispose();
        _core?.Dispose();
        _core = null;
        _detReq = _recReq = _quaReq = null;
        _detCompiled = _recCompiled = _quaCompiled = null;
    }

    private sealed class DetBox
    {
        public float X, Y, Width, Height, Confidence;
        public Point2f[] Landmarks = Array.Empty<Point2f>();
    }
}

/// <summary>单张人脸的处理结果。</summary>
public sealed class FaceResult
{
    public Rect Box { get; init; }
    public float Confidence { get; init; }
    public Point2f[] Landmarks { get; init; } = Array.Empty<Point2f>();
    public float[] Embedding { get; init; } = Array.Empty<float>();
    /// <summary>yaw / pitch / roll（quality 模型输出，参考值）。</summary>
    public float[]? Quality { get; init; }
}
