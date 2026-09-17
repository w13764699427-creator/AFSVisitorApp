using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using SkiaSharp;

namespace VisitorApp.Platforms.Windows
{
    public class IDReader
    {
        // 仅保留实际用到的 SDK 入口；SAM 管理 / 写卡等未用声明已删除（原样保留 P/Invoke 签名供需要时恢复）。
        [DllImport("sdtapi.dll")]
        public static extern int SDT_OpenPort(int iPortID);
        [DllImport("sdtapi.dll")]
        public static extern int SDT_ClosePort(int iPortID);

        [DllImport("sdtapi.dll")]
        public static extern int SDT_StartFindIDCard(int iPortID, byte[] pucIIN, int iIfOpen);
        [DllImport("sdtapi.dll")]
        public static extern int SDT_SelectIDCard(int iPortID, byte[] pucSN, int iIfOpen);
        [DllImport("sdtapi.dll")]
        public static extern int SDT_ReadBaseMsg(int iPortID, IntPtr pucCHMsg, ref int puiCHMsgLen, IntPtr pucPHMsg, ref int puiPHMsgLen, int iIfOpen);

        [DllImport("IDCUnpack.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int unpack(byte[] src, byte[] dst, int bmpSave);

        static IDReader()
        {
            // 读卡器 SDK 库放在应用目录或 Resources\Libs 下，显式指定查找路径避免加载失败。
            NativeLibrary.SetDllImportResolver(typeof(IDReader).Assembly, (name, assembly, searchPath) =>
            {
                if (!name.StartsWith("sdtapi", StringComparison.OrdinalIgnoreCase)
                    && !name.StartsWith("IDCUnpack", StringComparison.OrdinalIgnoreCase)
                    && !name.StartsWith("WltRS", StringComparison.OrdinalIgnoreCase))
                {
                    return IntPtr.Zero;
                }
                var candidates = new[]
                {
                    Path.Combine(AppContext.BaseDirectory, name),
                    Path.Combine(AppContext.BaseDirectory, "Resources", "Libs", name),
                };
                foreach (var candidate in candidates)
                {
                    if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle))
                        return handle;
                }
                return IntPtr.Zero;
            });
        }

        private static int isOpen = 1;               //自动开关串口
        /// <summary>最近一次读卡失败的原因（供上层展示具体错误），成功时为 null。</summary>
        public static string? LastError { get; private set; }

        /// <summary>最近一次读卡的过程诊断（各阶段返回值），用于排查证件照解码等问题。</summary>
        public static string Diagnostics { get; private set; } = string.Empty;

        /// <summary>
        /// 读卡全程串行锁：静态诊断状态（LastError / Diagnostics）与"临时切换进程工作目录给
        /// IDCUnpack.dll 找授权文件"都不允许并发交错，读卡期间其他线程的相对路径 I/O 也在锁内完成。
        /// </summary>
        private static readonly object _readLock = new object();

        public static ReadCompletedEventArgs? ReadIDCard()
        {
            lock (_readLock)
            {
                return ReadIDCardCore();
            }
        }

        private static ReadCompletedEventArgs? ReadIDCardCore()
        {
            try
            {
                LastError = null;
                Diagnostics = string.Empty;
                bool isUsbPort = false;
                int portNo = 0;
                int ret = 0;
                int portID = 0;
                // SDK 输出缓冲：IIN / SN 均为 8 字节，用 byte[8] 钉住完整缓冲（ref int 只有 4 字节，
                // 原生侧越界写会破坏相邻栈变量）。
                byte[] pucIIN = new byte[8];
                byte[] pucSN = new byte[8];
                ReadCompletedEventArgs cardInfo = new ReadCompletedEventArgs();
                //检测usb口的机具连接，必须先检测usb
                for (int port = 1001; port <= 1016; port++)
                {
                    portNo = SDT_OpenPort(port);
                    if (portNo == 144)
                    {
                        portID = port;
                        isUsbPort = true;
                        break;
                    }
                }
                //检测串口的机具连接
                if (!isUsbPort)
                {
                    for (int iPort = 1; iPort <= 2; iPort++)
                    {
                        portNo = SDT_OpenPort(iPort);
                        if (portNo == 144)
                        {
                            portID = iPort;
                            isUsbPort = false;
                            break;
                        }
                    }
                }
                if (portNo != 144)
                {
                    LastError = "未检测到读卡器设备：请检查 USB 连接与 SDT 驱动";
                    return null;
                }

                // 找卡 / 选卡 / 读信息：任何路径（含异常）都经 finally 关闭端口，不再泄漏已打开的串口。
                byte[] basicInfoBytes;
                byte[] phData;
                try
                {
                    //找卡
                    ret = SDT_StartFindIDCard(portID, pucIIN, isOpen);
                    if (ret != 159)
                    {
                        ret = SDT_StartFindIDCard(portID, pucIIN, isOpen);  //再找卡
                        if (ret != 159)
                        {
                            // 报真实找卡返回值，而不是随后 ClosePort 的返回值。
                            LastError = $"找卡失败(返回值 {ret})：请将身份证放置在读卡器感应区";
                            return null;
                        }
                    }

                    //选卡
                    ret = SDT_SelectIDCard(portID, pucSN, isOpen);
                    if (ret != 144)
                    {
                        ret = SDT_SelectIDCard(portID, pucSN, isOpen);  //再选卡
                        if (ret != 144)
                        {
                            LastError = $"选卡失败(返回值 {ret})：请重新放置身份证";
                            return null;
                        }
                    }

                    //读基本信息 + 照片
                    IntPtr hCHMsg = Marshal.AllocHGlobal(1024);
                    IntPtr hPHMsg = Marshal.AllocHGlobal(1024);
                    try
                    {
                        int puiCHMsgLen = 0, puiPHMsgLen = 0;
                        ret = SDT_ReadBaseMsg(portID, hCHMsg, ref puiCHMsgLen, hPHMsg, ref puiPHMsgLen, isOpen);
                        // 先校验返回值与长度，再拷贝：失败时长度字段可能是垃圾值，直接 Marshal.Copy 会越界读。
                        if (ret != 144)
                        {
                            LastError = $"读卡失败(返回值 {ret})：请重新放置身份证";
                            return null;
                        }
                        if (puiCHMsgLen < 0 || puiCHMsgLen > 1024 || puiPHMsgLen < 0 || puiPHMsgLen > 1024)
                        {
                            LastError = $"读卡返回异常长度(文本 {puiCHMsgLen} / 照片 {puiPHMsgLen})";
                            return null;
                        }
                        basicInfoBytes = new byte[puiCHMsgLen];
                        Marshal.Copy(hCHMsg, basicInfoBytes, 0, puiCHMsgLen);
                        phData = new byte[puiPHMsgLen];
                        Marshal.Copy(hPHMsg, phData, 0, puiPHMsgLen);
                        Diagnostics += $"ReadBaseMsg ret={ret}, chLen={puiCHMsgLen}, phLen={puiPHMsgLen}; ";
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(hCHMsg);
                        Marshal.FreeHGlobal(hPHMsg);
                    }
                }
                finally
                {
                    SDT_ClosePort(portID);
                }

                cardInfo.Name = System.Text.UnicodeEncoding.Unicode.GetString(basicInfoBytes, 0, Math.Min(30, basicInfoBytes.Length)).Trim();
                if (basicInfoBytes.Length >= 36)
                {
                    cardInfo.GenderCode = System.Text.UnicodeEncoding.Unicode.GetString(basicInfoBytes, 30, 2).Trim();
                    cardInfo.EthnicGroupCode = System.Text.UnicodeEncoding.Unicode.GetString(basicInfoBytes, 32, 4).Trim();
                }
                if (basicInfoBytes.Length >= 52)
                {
                    string birthDate = System.Text.UnicodeEncoding.Unicode.GetString(basicInfoBytes, 36, 16).Trim();
                    if (DateTime.TryParseExact(birthDate, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var birth))
                        cardInfo.BirthDate = birth;
                }
                if (basicInfoBytes.Length >= 122)
                {
                    cardInfo.Address = System.Text.UnicodeEncoding.Unicode.GetString(basicInfoBytes, 52, 70).Trim();
                }
                if (basicInfoBytes.Length >= 158)
                {
                    cardInfo.IDCard = System.Text.UnicodeEncoding.Unicode.GetString(basicInfoBytes, 122, 36).Trim();
                }
                if (basicInfoBytes.Length >= 188)
                {
                    cardInfo.Issued = System.Text.UnicodeEncoding.Unicode.GetString(basicInfoBytes, 158, 30).Trim();
                }
                // 有效期：防御性解析——数据异常时保留默认日期并记录诊断，不再让整次已成功的读卡作废。
                string effectiveDate = basicInfoBytes.Length > 188
                    ? System.Text.UnicodeEncoding.Unicode.GetString(basicInfoBytes, 188, basicInfoBytes.Length - 188).Trim()
                    : string.Empty;
                if (effectiveDate.Length >= 8 && DateTime.TryParseExact(effectiveDate.Substring(0, 8), "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var start))
                {
                    cardInfo.StartDate = start;
                    var expiry = effectiveDate.Length > 8 ? effectiveDate.Substring(8).Trim() : string.Empty;
                    if (expiry == "长期")
                    {
                        cardInfo.EndDate = DateTime.MaxValue;
                    }
                    else if (DateTime.TryParseExact(expiry, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var end))
                    {
                        cardInfo.EndDate = end;
                    }
                    else
                    {
                        Diagnostics += $"有效期数据无法解析({effectiveDate}); ";
                    }
                }
                else
                {
                    Diagnostics += "有效期数据无法解析; ";
                }

                byte[] output = new byte[102 * 126 * 3];
                // IDCUnpack.dll 按 "当前目录\idc_x64.lic" 查找授权文件（字符串分析已验证），
                // 授权文件与 DLL 同在 Resources\Libs：调用前临时切换工作目录，失败时兜底回应用根目录。
                var originalCwd = Environment.CurrentDirectory;
                try
                {
                    var libsDir = Path.Combine(AppContext.BaseDirectory, "Resources", "Libs");
                    if (Directory.Exists(libsDir)) Environment.CurrentDirectory = libsDir;
                    ret = unpack(phData, output, 0);
                    if (ret != 1)
                    {
                        Environment.CurrentDirectory = AppContext.BaseDirectory;
                        ret = unpack(phData, output, 0);
                    }
                }
                finally
                {
                    Environment.CurrentDirectory = originalCwd;
                }
                Diagnostics += $"unpack ret={ret}; ";
                if (ret == 1)
                {
                    // 垂直翻转 RGB 像素数据（102宽 × 126高）
                    for (int y = 0; y < 63; y++)
                    {
                        int topRow = y * 102 * 3;
                        int bottomRow = (125 - y) * 102 * 3;
                        for (int x = 0; x < 102 * 3; x++)
                        {
                            var temp = output[topRow + x];
                            output[topRow + x] = output[bottomRow + x];
                            output[bottomRow + x] = temp;
                        }
                    }

                    // unpack 输出为 RGB 顺序（旧项目 Color.FromArgb(r,g,b) 用法已验证），
                    // 逐像素写入 SKBitmap（Bgra8888 内存布局）。
                    using (var bitmap = new SKBitmap(102, 126, SKColorType.Bgra8888, SKAlphaType.Opaque))
                    {
                        var px = bitmap.GetPixels();
                        if (px != IntPtr.Zero)
                        {
                            for (int i = 0, o = 0; i < 102 * 126; i++, o += 4)
                            {
                                Marshal.WriteByte(px, o, output[i * 3 + 2]);       // B
                                Marshal.WriteByte(px, o + 1, output[i * 3 + 1]);   // G
                                Marshal.WriteByte(px, o + 2, output[i * 3]);       // R
                                Marshal.WriteByte(px, o + 3, 255);                 // A
                            }
                            using (var image = SKImage.FromBitmap(bitmap))
                            using (var data = image?.Encode(SKEncodedImageFormat.Jpeg, 90))
                            {
                                if (data is not null) cardInfo.PhotoData = data.ToArray();
                            }
                        }
                    }
                    Diagnostics += $"PhotoData={(cardInfo.PhotoData is null ? "编码失败" : cardInfo.PhotoData.Length + "字节")}; ";
                }             
                else
                {
                    Diagnostics += "证件照解码失败; ";
                }
                return cardInfo;
            }
            catch (DllNotFoundException)
            {
                // 缺少 sdtapi.dll / WltRS.dll / IDCard_Unpack.dll 等 SDK 库：向上抛出，由服务层给出明确提示。
                LastError = "读卡器 SDK 库缺失";
                throw;
            }
            catch (BadImageFormatException)
            {
                // DLL 位宽与进程不符（如 32 位 dll 加载到 64 位进程）。
                LastError = "读卡器 SDK 库位宽不匹配（需要 64 位版本）";
                throw;
            }
            catch (Exception ex)
            {
                LastError = $"读卡异常：{ex.Message}";
                return null;
            }
        }
    }

    public class ReadCompletedEventArgs : EventArgs
    {
        private static SortedList<string, string> EthnicGroupList = new SortedList<string, string>();
        static ReadCompletedEventArgs()
        {
            EthnicGroupList.Add("01", "汉族");
            EthnicGroupList.Add("02", "蒙古族");
            EthnicGroupList.Add("03", "回族");
            EthnicGroupList.Add("04", "藏族");
            EthnicGroupList.Add("05", "维吾尔族");
            EthnicGroupList.Add("06", "苗族");
            EthnicGroupList.Add("07", "彝族");
            EthnicGroupList.Add("08", "壮族");
            EthnicGroupList.Add("09", "布依族");
            EthnicGroupList.Add("10", "朝鲜族");
            EthnicGroupList.Add("11", "满族");
            EthnicGroupList.Add("12", "侗族");
            EthnicGroupList.Add("13", "瑶族");
            EthnicGroupList.Add("14", "白族");
            EthnicGroupList.Add("15", "土家族");
            EthnicGroupList.Add("16", "哈尼族");
            EthnicGroupList.Add("17", "哈萨克族");
            EthnicGroupList.Add("18", "傣族");
            EthnicGroupList.Add("19", "黎族");
            EthnicGroupList.Add("20", "傈僳族");
            EthnicGroupList.Add("21", "佤族");
            EthnicGroupList.Add("22", "畲族");
            EthnicGroupList.Add("23", "高山族");
            EthnicGroupList.Add("24", "拉祜族");
            EthnicGroupList.Add("25", "水族");
            EthnicGroupList.Add("26", "东乡族");
            EthnicGroupList.Add("27", "纳西族");
            EthnicGroupList.Add("28", "景颇族");
            EthnicGroupList.Add("29", "柯尔克孜族");
            EthnicGroupList.Add("30", "土族");
            EthnicGroupList.Add("31", "达翰尔族");
            EthnicGroupList.Add("32", "仫佬族");
            EthnicGroupList.Add("33", "羌族");
            EthnicGroupList.Add("34", "布朗族");
            EthnicGroupList.Add("35", "撒拉族");
            EthnicGroupList.Add("36", "毛南族");
            EthnicGroupList.Add("37", "仡佬族");
            EthnicGroupList.Add("38", "锡伯族");
            EthnicGroupList.Add("39", "阿昌族");
            EthnicGroupList.Add("40", "普米族");
            EthnicGroupList.Add("41", "塔吉克族");
            EthnicGroupList.Add("42", "怒族");
            EthnicGroupList.Add("43", "乌孜别克族");
            EthnicGroupList.Add("44", "俄罗斯族");
            EthnicGroupList.Add("45", "鄂温克族");
            EthnicGroupList.Add("46", "德昂族");
            EthnicGroupList.Add("47", "保安族");
            EthnicGroupList.Add("48", "裕固族");
            EthnicGroupList.Add("49", "京族");
            EthnicGroupList.Add("50", "塔塔尔族");
            EthnicGroupList.Add("51", "独龙族");
            EthnicGroupList.Add("52", "鄂伦春族");
            EthnicGroupList.Add("53", "赫哲族");
            EthnicGroupList.Add("54", "门巴族");
            EthnicGroupList.Add("55", "珞巴族");
            EthnicGroupList.Add("56", "基诺族");
            EthnicGroupList.Add("57", "其它");
            EthnicGroupList.Add("98", "外国人入籍");
        }

        public string Name { get; set; } = string.Empty;
        public string GenderCode{ get; set; } = string.Empty;
  
        public string GenderName
        {
            get
            {
                string genderName = string.Empty;
                switch (GenderCode)
                {
                    case "1":
                        genderName = "男";
                        break;
                    case "2":
                        genderName = "女";
                        break;
                }
                return genderName;
            }
        }
        public string IDCard { get; set; } = string.Empty;
        public string EthnicGroupCode { get; set; } = string.Empty;

        public string EthnicGroupName
        {
            get
            {
                if (EthnicGroupList.ContainsKey(EthnicGroupCode))
                    return EthnicGroupList[EthnicGroupCode];
                return string.Empty;
            }
        }

        public DateTime BirthDate { get; set; }
        public string Address { get; set; } = string.Empty;
        public string Issued { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public string ExpiryDateCode
        {
            get
            {
                string expiryDateCode = string.Empty;
                if (EndDate == DateTime.MaxValue)
                {
                    expiryDateCode = "3";
                }
                else
                {
                    if (StartDate != DateTime.MinValue)
                    {
                        switch (EndDate.AddDays(1).Year - StartDate.Year)
                        {
                            case 5:
                                expiryDateCode = "4";
                                break;
                            case 10:
                                expiryDateCode = "1";
                                break;
                            case 20:
                                expiryDateCode = "2";
                                break;
                        }
                    }
                }
                return expiryDateCode;
            }

        }
        public string ExpiryDateName
        {
            get
            {
                string expiryDateName = string.Empty;
                if (EndDate == DateTime.MaxValue)
                {
                    expiryDateName = "长期";
                }
                else
                {
                    if (StartDate != DateTime.MinValue)
                    {
                        switch (EndDate.AddDays(1).Year - StartDate.Year)
                        {
                            case 5:
                                expiryDateName = "5年";
                                break;
                            case 10:
                                expiryDateName = "10年";
                                break;
                            case 20:
                                expiryDateName = "20年";
                                break;
                        }
                    }
                }
                return expiryDateName;
            }

        }
        public byte[] PhotoData { get; set; } = Array.Empty<byte>();
    }
}
