#if WINDOWS
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using VisitorApp.Models;

namespace VisitorApp.Services;

/// <summary>
/// 人脸比对服务（Windows）：SCRFD(det_10g) 检测 → 五点关键点对齐 → ArcFace(w600k_r50) 特征 → 余弦相似度。
/// 逻辑移植自 ONNXDemo（OpenVINO 版），检测/识别管线保持一致；
/// Demo 中 1k3d68 姿态估计分支的结果未被使用，故不加载该模型。
/// </summary>
public sealed class FaceCompareService : IFaceCompareService, IDisposable
{
    // det_10g.onnx 为 640×640 固定输入（输出锚点数 640/640=20 已验证），
    // 传入其他尺寸会导致输出形状不匹配、解码错位而检不到人脸。
    private const int DetWidth = 640;
    private const int DetHeight = 640;
    private const float DetScoreThreshold = 0.5f;
    private const float NmsIouThreshold = 0.4f;
    private const int AlignSize = 112;

    private readonly object _initLock = new();
    private InferenceSession? _detSession;
    private InferenceSession? _recSession;
    private string? _initError;

    /// <summary>实际使用的推理加速器（"OpenVINO GPU" / "CPU"），供诊断展示。</summary>
    public string ExecutionProvider { get; private set; } = string.Empty;

    public bool IsSupported => true;

    /// <summary>比对两张照片中的人脸（均为 base64 data URL），返回余弦相似度。</summary>
    public async Task<FaceCompareResult> CompareAsync(string imageDataUrl1, string imageDataUrl2)
    {
        try
        {
            return await Task.Run(() =>
            {
                var initError = EnsureSessions();
                if (initError is not null) return FaceCompareResult.Fail(initError);

                var feature1 = GetFeature(DecodeImage(imageDataUrl1));
                if (feature1 is null) return FaceCompareResult.Fail("照片 1 未检测到人脸");

                var feature2 = GetFeature(DecodeImage(imageDataUrl2));
                if (feature2 is null) return FaceCompareResult.Fail("照片 2 未检测到人脸");

                var similarity = System.Numerics.Tensors.TensorPrimitives.CosineSimilarity(feature1, feature2);
                return FaceCompareResult.Ok(similarity);
            });
        }
        catch (Exception ex)
        {
            return FaceCompareResult.Fail($"人脸比对异常：{ex.Message}");
        }
    }

    /// <summary>懒加载检测 / 识别两个会话（首次调用时初始化）。</summary>
    private string? EnsureSessions()
    {
        if (_detSession is not null && _recSession is not null) return null;
        if (_initError is not null) return _initError;

        lock (_initLock)
        {
            if (_detSession is not null && _recSession is not null) return null;
            if (_initError is not null) return _initError;

            var detPath = FindModel("det_10g.onnx");
            var recPath = FindModel("w600k_r50.onnx");
            if (detPath is null || recPath is null)
            {
                _initError = "人脸模型缺失：请将 det_10g.onnx / w600k_r50.onnx 放到应用目录或 D:\\temp";
                return _initError;
            }

            try
            {
                // 优先 OpenVINO GPU（与 Demo 一致），会话内同时挂 CPU 作为节点级兜底；
                // OpenVINO 运行时不可用（缺库/无支持硬件）时整体回落纯 CPU。
                try
                {
                    using var options = new SessionOptions();
                    options.AppendExecutionProvider_OpenVINO("GPU");
                    options.AppendExecutionProvider_CPU();
                    _detSession = new InferenceSession(detPath, options);
                    _recSession = new InferenceSession(recPath, options);
                    ExecutionProvider = "OpenVINO GPU";
                }
                catch (Exception gpuEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[FaceCompare] OpenVINO GPU 不可用，回落 CPU：{gpuEx.Message}");
                    _detSession?.Dispose();
                    _recSession?.Dispose();
                    using var options = new SessionOptions();
                    _detSession = new InferenceSession(detPath, options);
                    _recSession = new InferenceSession(recPath, options);
                    ExecutionProvider = "CPU";
                }
                return null;
            }
            catch (Exception ex)
            {
                _initError = $"人脸模型加载失败：{ex.Message}";
                return _initError;
            }
        }
    }

    /// <summary>模型查找顺序：应用目录 → 应用目录\Resources\Models → D:\temp。</summary>
    private static string? FindModel(string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.Combine(AppContext.BaseDirectory, "Resources", "Models", fileName),
            Path.Combine(@"D:\temp", fileName),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>base64 data URL → OpenCV Mat。</summary>
    private static Mat? DecodeImage(string dataUrl)
    {
        if (string.IsNullOrEmpty(dataUrl)) return null;
        var comma = dataUrl.IndexOf(',');
        var base64 = comma >= 0 ? dataUrl[(comma + 1)..] : dataUrl;
        try
        {
            var bytes = Convert.FromBase64String(base64);
            var mat = Cv2.ImDecode(bytes, ImreadModes.Color);
            return mat.Empty() ? null : mat;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>对单张图提取 512 维人脸特征（检测 → 对齐 → 识别），未检测到人脸返回 null。</summary>
    private float[]? GetFeature(Mat? rawImg)
    {
        if (rawImg is null) return null;
        using (rawImg)
        using (var imgMat = PadToSize(rawImg, new OpenCvSharp.Size(DetWidth, DetHeight)))
        {
            // ===== 1. SCRFD 检测 =====
            var inputTensor = ImageToTensor(imgMat, DetWidth, DetHeight, true);
            using var detResults = _detSession!.Run(new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input.1", inputTensor)
            });

            int[] strides = { 8, 16, 32 };
            const int fmc = 3;
            var boxList = new List<BoundingBox>();

            for (var idx = 0; idx < strides.Length; idx++)
            {
                var stride = strides[idx];
                var scores = detResults[idx].AsTensor<float>();
                // 分数张量每个位置 2 个锚点，总长 = 2 × blockSize²，据此反推每行锚点数。
                var blockSize = (int)Math.Sqrt(scores.Length / 2.0);
                var boxes = detResults[idx + fmc].AsTensor<float>().ToArray();
                var lmkArray = detResults[idx + fmc * 2].AsTensor<float>().ToArray();

                for (var i = 0; i < scores.Length; i++)
                {
                    var score = scores[i];
                    if (score < DetScoreThreshold) continue;

                    var col = (i / 2) % blockSize;
                    var row = (i / 2) / blockSize;
                    var centerX = col * stride;
                    var centerY = row * stride;

                    var x1 = centerX - boxes[i * 4] * stride;
                    var y1 = centerY - boxes[i * 4 + 1] * stride;
                    var x2 = centerX + boxes[i * 4 + 2] * stride;
                    var y2 = centerY + boxes[i * 4 + 3] * stride;

                    boxList.Add(new BoundingBox
                    {
                        X = x1,
                        Y = y1,
                        Right = x2,
                        Bottom = y2,
                        Width = x2 - x1,
                        Height = y2 - y1,
                        Confidence = score,
                        Landmarks = TensorToLandmarks(lmkArray, i, centerX, centerY, stride).ToArray()
                    });
                }
            }

            if (boxList.Count == 0) return null;

            // ===== 2. NMS 后取置信度最高的一张脸 =====
            var best = Suppress(boxList, NmsIouThreshold)[0];

            // ===== 3. 五点对齐到 112x112（ArcFace 标准模板） =====
            using var aligned = AlignFace(imgMat, best.Landmarks.ToList(), AlignSize);

            // ===== 4. ArcFace 特征提取（w600k_r50 需标准预处理 (x-127.5)/127.5，
            //       实测 0~1 归一化时特征塌缩、不同人相似度虚高到 0.61，无法区分同人/异人） =====
            var faceTensor = ImageToTensor(aligned, AlignSize, AlignSize, false, arcfaceNorm: true);
            using var recResults = _recSession!.Run(new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input.1", faceTensor)
            });
            return recResults.First().AsTensor<float>().ToArray();
        }
    }

    /// <summary>NMS：按置信度从高到低保留，抑制 IoU 超阈值的重叠框。</summary>
    private static List<BoundingBox> Suppress(List<BoundingBox> boxes, float iouThreshold)
    {
        var sortedBoxes = boxes.OrderByDescending(b => b.Confidence).ToList();
        var selectedBoxes = new List<BoundingBox>();

        while (sortedBoxes.Count > 0)
        {
            var currentBox = sortedBoxes[0];
            selectedBoxes.Add(currentBox);
            sortedBoxes.RemoveAt(0);
            sortedBoxes = sortedBoxes.Where(box => CalculateIoU(currentBox, box) <= iouThreshold).ToList();
        }
        return selectedBoxes;
    }

    private static float CalculateIoU(BoundingBox box1, BoundingBox box2)
    {
        float x1 = Math.Max(box1.X, box2.X);
        float y1 = Math.Max(box1.Y, box2.Y);
        float x2 = Math.Min(box1.X + box1.Width, box2.X + box2.Width);
        float y2 = Math.Min(box1.Y + box1.Height, box2.Y + box2.Height);

        float intersection = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
        float area1 = box1.Width * box1.Height;
        float area2 = box2.Width * box2.Height;
        float union = area1 + area2 - intersection;

        return union > 0 ? intersection / union : 0;
    }

    /// <summary>等比缩放后居中补黑边到目标尺寸（保证检测输入恒定 480x640）。</summary>
    private static Mat PadToSize(Mat src, OpenCvSharp.Size targetSize)
    {
        if (src.Size() == targetSize)
            return src.Clone();

        double rx = targetSize.Width * 1.0 / src.Width;
        double ry = targetSize.Height * 1.0 / src.Height;
        double rate = Math.Min(rx, ry);

        Mat? resizedMat = null;
        var workingMat = src;

        if (rate < 1.0)
        {
            resizedMat = src.Resize(new OpenCvSharp.Size(0, 0), rate, rate);
            workingMat = resizedMat;
        }

        try
        {
            int padWidth = targetSize.Width - workingMat.Width;
            int padHeight = targetSize.Height - workingMat.Height;

            int top = padHeight / 2;
            int bottom = padHeight - top;
            int left = padWidth / 2;
            int right = padWidth - left;

            var dst = new Mat();
            Cv2.CopyMakeBorder(workingMat, dst, top, bottom, left, right, BorderTypes.Constant, new Scalar(0, 0, 0));
            return dst;
        }
        finally
        {
            resizedMat?.Dispose();
        }
    }

    /// <summary>
    /// BGR Mat → NCHW float 张量。isChange 为 true 时归一化到 0~1（检测模型用）；
    /// arcfaceNorm 为 true 时按 ArcFace 标准 (x-127.5)/127.5 归一化（识别模型用，
    /// 实测 0~1 归一化会导致不同人相似度虚高，无法区分同人/异人）。
    /// </summary>
    private static Tensor<float> ImageToTensor(Mat img, int width, int height, bool isChange, bool arcfaceNorm = false)
    {
        using var resized = PadToSize(img, new OpenCvSharp.Size(width, height));
        double scale = arcfaceNorm ? (1.0 / 127.5) : (isChange ? (1.0 / 255.0) : 1.0);
        // ConvertTo: dst = src * scale + beta；(x-127.5)/127.5 = x/127.5 - 1。
        double shift = arcfaceNorm ? -1.0 : 0.0;

        using var converted = new Mat();
        resized.ConvertTo(converted, MatType.CV_32FC3, scale, shift);

        int channelSize = width * height;
        var data = new float[3 * channelSize];

        Cv2.Split(converted, out var channels);
        try
        {
            for (var c = 0; c < 3; c++)
            {
                Marshal.Copy(channels[c].Data, data, c * channelSize, channelSize);
            }
        }
        finally
        {
            for (var c = 0; c < 3; c++)
            {
                channels[c]?.Dispose();
            }
        }

        return new DenseTensor<float>(data, new[] { 1, 3, height, width });
    }

    /// <summary>从检测输出中还原第 index 个锚点的 5 个关键点坐标。</summary>
    private static List<Point2f> TensorToLandmarks(float[] landmarks, int index, float centerX, float centerY, int stride)
    {
        int offset = index * 10;
        var pts = new List<Point2f>();
        for (var i = 0; i < 5; i++)
        {
            pts.Add(new Point2f(
                centerX + landmarks[offset + i * 2] * stride,
                centerY + landmarks[offset + i * 2 + 1] * stride
            ));
        }
        return pts;
    }

    /// <summary>按 ArcFace 标准五点模板做仿射对齐，输出 size×size 正脸。</summary>
    private static Mat AlignFace(Mat img, List<Point2f> landmarks, int size)
    {
        var arcfaceStd = new[]
        {
            new Point2f(38.2946f, 51.6963f),
            new Point2f(73.5318f, 51.5014f),
            new Point2f(56.0252f, 71.7366f),
            new Point2f(41.5493f, 92.3655f),
            new Point2f(70.7299f, 92.2041f)
        };

        using var m = Cv2.EstimateAffinePartial2D(InputArray.Create(landmarks), InputArray.Create(arcfaceStd));
        var aligned = new Mat();
        if (m is null)
        {
            // 对齐矩阵计算失败时退化为直接缩放到目标尺寸，保证识别模型输入恒定。
            Cv2.Resize(img, aligned, new OpenCvSharp.Size(size, size));
            return aligned;
        }
        Cv2.WarpAffine(img, aligned, m, new OpenCvSharp.Size(size, size));
        return aligned;
    }

    public void Dispose()
    {
        _detSession?.Dispose();
        _recSession?.Dispose();
    }

    private sealed class BoundingBox
    {
        public float X { get; init; }
        public float Y { get; init; }
        public float Width { get; init; }
        public float Height { get; init; }
        public float Right { get; init; }
        public float Bottom { get; init; }
        public float Confidence { get; init; }
        public Point2f[] Landmarks { get; init; } = Array.Empty<Point2f>();
    }
}

#else

using VisitorApp.Models;

namespace VisitorApp.Services;

/// <summary>非 Windows 平台占位实现：不支持本地人脸比对。</summary>
public sealed class FaceCompareService : IFaceCompareService
{
    public bool IsSupported => false;

    public string ExecutionProvider => string.Empty;

    public Task<FaceCompareResult> CompareAsync(string imageDataUrl1, string imageDataUrl2) =>
        Task.FromResult(FaceCompareResult.Fail("当前平台不支持本地人脸比对"));
}

#endif
