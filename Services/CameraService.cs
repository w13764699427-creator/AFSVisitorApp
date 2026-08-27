namespace VisitorApp.Services;

/// <summary>
/// 拍照结果，UI / OCR 服务都从这里消费。
/// </summary>
public record CapturedPhoto(
    byte[] Bytes,
    string ContentType,
    string DataUrl,
    string FileName,
    long SizeBytes);

public interface ICameraService
{
    /// <summary>设备是否具备拍照能力（无摄像头 / 不支持时返回 false）。</summary>
    bool IsCaptureSupported { get; }

    /// <summary>调起系统相机拍一张照片（用于身份证扫描）；用户取消时返回 null。</summary>
    Task<CapturedPhoto?> CapturePhotoAsync(CancellationToken ct = default);

    /// <summary>从系统相册挑选一张图片，便于桌面端无摄像头时回退使用。</summary>
    Task<CapturedPhoto?> PickPhotoAsync(CancellationToken ct = default);
}

/// <summary>
/// 基于 MAUI Essentials MediaPicker 的拍照实现 —— 仅用于身份证扫描。
/// 人脸拍照已改用 JS WebRTC (CameraPreview.razor + camera.js)。
/// </summary>
public class CameraService : ICameraService
{
    public bool IsCaptureSupported => MediaPicker.Default.IsCaptureSupported;

    public async Task<CapturedPhoto?> CapturePhotoAsync(CancellationToken ct = default)
    {
        if (!MediaPicker.Default.IsCaptureSupported)
        {
            return await PickPhotoAsync(ct);
        }

        try
        {
            var file = await MediaPicker.Default.CapturePhotoAsync(new MediaPickerOptions
            {
                Title = "请将身份证正面对准摄像头"
            });
            return await ToCapturedAsync(file);
        }
        catch (FeatureNotSupportedException)
        {
            return await PickPhotoAsync(ct);
        }
        catch (PermissionException)
        {
            throw;
        }
    }

    public async Task<CapturedPhoto?> PickPhotoAsync(CancellationToken ct = default)
    {
        var files = await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions
        {
            Title = "请选择一张图片"
        });
        var file = files?.FirstOrDefault();
        return await ToCapturedAsync(file);
    }

    private static async Task<CapturedPhoto?> ToCapturedAsync(FileResult? file)
    {
        if (file is null) return null;

        await using var stream = await file.OpenReadAsync();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        var bytes = ms.ToArray();

        var contentType = string.IsNullOrWhiteSpace(file.ContentType)
            ? "image/jpeg"
            : file.ContentType;

        var dataUrl = $"data:{contentType};base64,{Convert.ToBase64String(bytes)}";

        return new CapturedPhoto(
            Bytes: bytes,
            ContentType: contentType,
            DataUrl: dataUrl,
            FileName: file.FileName ?? $"photo-{DateTime.Now:yyyyMMddHHmmss}.jpg",
            SizeBytes: bytes.LongLength);
    }
}
