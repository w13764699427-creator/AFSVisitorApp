using VisitorApp.Models;

namespace VisitorApp.Services;

/// <summary>
/// 身份证读卡器 — Windows 平台实现。
/// 通过 P/Invoke 调用 sdtapi.dll 读取二代身份证。
/// 失败时抛出异常并携带具体原因（SDK 库缺失 / 设备未连接 / 未放卡等），由界面展示。
/// </summary>
public class IdCardReaderService : IIdCardReaderService
{
    public Task<IdCardReadResult?> ReadIdCardAsync()
    {
        return Task.Run<IdCardReadResult?>(() =>
        {
            try
            {
                var card = Platforms.Windows.IDReader.ReadIDCard();
                if (card is null)
                {
                    // SDTAPI 层已记录具体失败原因，抛给界面展示，避免一律显示"请确认身份证已放置好"。
                    throw new InvalidOperationException(
                        Platforms.Windows.IDReader.LastError ?? "读卡失败，请确认身份证已放置好");
                }

                var result = new IdCardReadResult
                {
                    Name = card.Name,
                    Gender = card.GenderName,
                    IdNumber = card.IDCard,
                    EthnicGroup = card.EthnicGroupName,
                    Birthday = card.BirthDate != DateTime.MinValue
                        ? card.BirthDate.ToString("yyyy-MM-dd") : string.Empty,
                    Address = card.Address,
                    IssuingAuthority = card.Issued,
                    StartDate = card.StartDate != DateTime.MinValue
                        ? card.StartDate.ToString("yyyy-MM-dd") : string.Empty,
                    EndDate = card.EndDate == DateTime.MaxValue
                        ? "长期"
                        : card.EndDate.ToString("yyyy-MM-dd"),
                };

                // 将证件照 byte[] 转为 base64 data URL
                if ((card.PhotoData?.Length ?? 0) > 0)
                {
                    result.PhotoBase64 = "data:image/jpeg;base64,"
                        + Convert.ToBase64String(card.PhotoData);
                }

                return result;
            }
            catch (DllNotFoundException ex)
            {
                throw new InvalidOperationException(
                    "未找到读卡器 SDK 库（sdtapi.dll 等）：请将 64 位 sdtapi.dll / WltRS.dll / IDCard_Unpack.dll 放到应用目录。" +
                    $" 详细信息：{ex.Message}");
            }
            catch (BadImageFormatException ex)
            {
                throw new InvalidOperationException(
                    "读卡器 SDK 库位宽不匹配：应用为 64 位，请更换 64 位版本的 sdtapi.dll 等。" +
                    $" 详细信息：{ex.Message}");
            }
        });
    }
}
