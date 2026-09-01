#if WINDOWS
using OpenCvSharp;
using VisitorApp.Models;

namespace VisitorApp.Services;

/// <summary>
/// 人脸比对服务（Windows）：OpenVINO 新模型管线（detect 检测 → 五点对齐 → extract 特征 → 余弦相似度），
/// 引擎移植自 FaceCompareTest 已验证实现。推理设备可在「系统设置」配置（CPU / GPU / AUTO），
/// GPU 不可用时自动回退 CPU 并在 FallbackNote 中给出说明。
/// </summary>
public sealed class FaceCompareService : IFaceCompareService, IDisposable
{
    private readonly object _initLock = new();
    private OpenVinoFaceEngine? _engine;
    private string? _initError;
    private string _initDevice = string.Empty;

    /// <summary>实际使用的推理设备（含回退结果），供诊断展示。</summary>
    public string ExecutionProvider { get; private set; } = string.Empty;

    /// <summary>设备回退说明（如 GPU 不可用自动回落 CPU），无回退时为 null。</summary>
    public string? FallbackNote => _engine?.FallbackNote;

    public bool IsSupported => true;

    /// <summary>比对两张照片中的人脸（均为 base64 data URL），返回余弦相似度。</summary>
    public async Task<FaceCompareResult> CompareAsync(string imageDataUrl1, string imageDataUrl2)
    {
        try
        {
            return await Task.Run(() =>
            {
                var initError = EnsureEngine();
                if (initError is not null) return FaceCompareResult.Fail(initError);

                var engine = _engine!;
                lock (engine)   // 引擎非线程安全：同一时刻只允许一次推理
                {
                    var face1 = engine.Process(DecodeImage(imageDataUrl1), withQuality: false);
                    if (face1 is null) return FaceCompareResult.Fail("照片 1 未检测到人脸");

                    var face2 = engine.Process(DecodeImage(imageDataUrl2), withQuality: false);
                    if (face2 is null) return FaceCompareResult.Fail("照片 2 未检测到人脸");

                    return FaceCompareResult.Ok(OpenVinoFaceEngine.Cosine(face1.Embedding, face2.Embedding));
                }
            });
        }
        catch (Exception ex)
        {
            return FaceCompareResult.Fail($"人脸比对异常：{ex.Message}");
        }
    }

    /// <summary>懒加载引擎（首次比对时初始化）；设置页修改设备后通过 Reset 重建。</summary>
    private string? EnsureEngine()
    {
        var configuredDevice = FaceInferenceDeviceStore.Load();
        if (_engine is not null && _initDevice == configuredDevice && _initError is null) return null;
        if (_initError is not null && _initDevice == configuredDevice) return _initError;

        lock (_initLock)
        {
            configuredDevice = FaceInferenceDeviceStore.Load();
            if (_engine is not null && _initDevice == configuredDevice && _initError is null) return null;
            if (_initError is not null && _initDevice == configuredDevice) return _initError;

            var modelsDir = FindModelsDir();
            if (modelsDir is null)
            {
                _initDevice = configuredDevice;
                _initError = "人脸模型缺失：请将 detect / extract / quality 模型（.xml+.bin）放到应用目录的 Resources\\ModelsOpenVino";
                return _initError;
            }

            try
            {
                _engine?.Dispose();
                var engine = new OpenVinoFaceEngine();
                engine.Init(modelsDir, configuredDevice);
                _engine = engine;
                _initDevice = configuredDevice;
                _initError = null;
                ExecutionProvider = engine.Device;
                if (engine.FallbackNote is not null)
                    System.Diagnostics.Debug.WriteLine($"[FaceCompare] {engine.FallbackNote}");
                return null;
            }
            catch (Exception ex)
            {
                _initDevice = configuredDevice;
                _initError = $"人脸模型加载失败：{ex.Message}";
                return _initError;
            }
        }
    }

    /// <summary>模型目录查找顺序：应用目录\Resources\ModelsOpenVino → 应用目录 → D:\temp。</summary>
    private static string? FindModelsDir()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Resources", "ModelsOpenVino"),
            AppContext.BaseDirectory,
            Path.Combine(@"D:\temp", "ModelsOpenVino"),
        };
        return candidates.FirstOrDefault(d => File.Exists(Path.Combine(d, "detect.xml")));
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

    public void Dispose()
    {
        _engine?.Dispose();
        _engine = null;
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

    /// <summary>设备回退说明，占位实现恒为 null。</summary>
    public string? FallbackNote => null;

    public Task<FaceCompareResult> CompareAsync(string imageDataUrl1, string imageDataUrl2) =>
        Task.FromResult(FaceCompareResult.Fail("当前平台不支持本地人脸比对"));
}

#endif
