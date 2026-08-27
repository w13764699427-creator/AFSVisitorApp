using VisitorApp.Models;

namespace VisitorApp.Services;

/// <summary>
/// 身份证读卡器 — Windows 平台实现。
/// 通过 P/Invoke 调用 sdtapi.dll 读取二代身份证。
/// </summary>
public class IdCardReaderService : IIdCardReaderService
{
    public Task<IdCardReadResult?> ReadIdCardAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                var card = Platforms.Windows.IDReader.ReadIDCard();
                if (card is null) return null;

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
                if (card.PhotoData is { Length: > 0 })
                {
                    result.PhotoBase64 = "data:image/jpeg;base64,"
                        + Convert.ToBase64String(card.PhotoData);
                }

                return result;
            }
            catch
            {
                return null;
            }
        });
        return Task.FromResult<IdCardReadResult?>(null);
    }
}
