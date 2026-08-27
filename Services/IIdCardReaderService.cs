using VisitorApp.Models;

namespace VisitorApp.Services;

/// <summary>
/// 身份证读卡器服务接口。
/// Windows 平台通过 P/Invoke 调用 sdtapi.dll 读取二代身份证。
/// 非 Windows 平台返回 null。
/// </summary>
public interface IIdCardReaderService
{
    Task<IdCardReadResult?> ReadIdCardAsync();
}
