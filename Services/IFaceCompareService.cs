using VisitorApp.Models;

namespace VisitorApp.Services;

/// <summary>
/// 人脸比对服务：对两张照片（base64 data URL）分别提取人脸特征并计算余弦相似度。
/// Windows 平台基于 ONNX Runtime（SCRFD 检测 + ArcFace 识别）；其他平台返回不支持。
/// </summary>
public interface IFaceCompareService
{
    /// <summary>当前平台是否支持本地人脸比对。</summary>
    bool IsSupported { get; }

    /// <summary>实际使用的推理加速器（如 "OpenVINO GPU" / "CPU"），未初始化时为空。</summary>
    string ExecutionProvider { get; }

    /// <summary>比对两张照片中的人脸，返回相似度结果。</summary>
    Task<FaceCompareResult> CompareAsync(string imageDataUrl1, string imageDataUrl2);
}
