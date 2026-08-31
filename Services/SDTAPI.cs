using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using SkiaSharp;

namespace VisitorApp.Platforms.Windows
{
    public class IDReader
    {
        [DllImport("sdtapi.dll")]
        public static extern int SDT_OpenPort(int iPortID);
        [DllImport("sdtapi.dll")]
        public static extern int SDT_ClosePort(int iPortID);
        [DllImport("sdtapi.dll")]
        public static extern int SDT_PowerManagerBegin(int iPortID, int iIfOpen);
        [DllImport("sdtapi.dll")]
        public static extern int SDT_AddSAMUser(int iPortID, string pcUserName, int iIfOpen);
        [DllImport("sdtapi.dll")]
        public static extern int SDT_SAMLogin(int iPortID, string pcUserName, string pcPasswd, int iIfOpen);
        [DllImport("sdtapi.dll")]
        public static extern int SDT_SAMLogout(int iPortID, int iIfOpen);
        [DllImport("sdtapi.dll")]
        public static extern int SDT_UserManagerOK(int iPortID, int iIfOpen);
        [DllImport("sdtapi.dll")]
        public static extern int SDT_ChangeOwnPwd(int iPortID, string pcOldPasswd, string pcNewPasswd, int iIfOpen);
        [DllImport("sdtapi.dll")]
        public static extern int SDT_ChangeOtherPwd(int iPortID, string pcUserName, string pcNewPasswd, int iIfOpen);
        [DllImport("sdtapi.dll")]
        public static extern int SDT_DeleteSAMUser(int iPortID, string pcUserName, int iIfOpen);

        [DllImport("sdtapi.dll")]
        public static extern int SDT_StartFindIDCard(int iPortID, ref int pucIIN, int iIfOpen);
        [DllImport("sdtapi.dll")]
        public static extern int SDT_SelectIDCard(int iPortID, ref int pucSN, int iIfOpen);
        [DllImport("sdtapi.dll")]
        public static extern int SDT_ReadBaseMsg(int iPortID, IntPtr pucCHMsg, ref int puiCHMsgLen, IntPtr pucPHMsg, ref int puiPHMsgLen, int iIfOpen);
        [DllImport("sdtapi.dll")]
        public static extern int SDT_ReadBaseMsgToFile(int iPortID, string fileName1, ref int puiCHMsgLen, string fileName2, ref int puiPHMsgLen, int iIfOpen);

        [DllImport("sdtapi.dll")]
        public static extern int SDT_WriteAppMsg(int iPortID, ref byte pucSendData, int uiSendLen, ref byte pucRecvData, ref int puiRecvLen, int iIfOpen);
        [DllImport("sdtapi.dll")]
        public static extern int SDT_WriteAppMsgOK(int iPortID, ref byte pucData, int uiLen, int iIfOpen);

        [DllImport("sdtapi.dll")]
        public static extern int SDT_CancelWriteAppMsg(int iPortID, int iIfOpen);
        [DllImport("sdtapi.dll")]
        public static extern int SDT_ReadNewAppMsg(int iPortID, ref byte pucAppMsg, ref int puiAppMsgLen, int iIfOpen);
        [DllImport("sdtapi.dll")]
        public static extern int SDT_ReadAllAppMsg(int iPortID, ref byte pucAppMsg, ref int puiAppMsgLen, int iIfOpen);
        [DllImport("sdtapi.dll")]
        public static extern int SDT_UsableAppMsg(int iPortID, ref byte ucByte, int iIfOpen);

        [DllImport("sdtapi.dll")]
        public static extern int SDT_GetUnlockMsg(int iPortID, ref byte strMsg, int iIfOpen);
        [DllImport("sdtapi.dll")]
        public static extern int SDT_GetSAMID(int iPortID, ref byte StrSAMID, int iIfOpen);

        [DllImport("sdtapi.dll")]
        public static extern int SDT_SetMaxRFByte(int iPortID, byte ucByte, int iIfOpen);
        [DllImport("sdtapi.dll")]
        public static extern int SDT_ResetSAM(int iPortID, int iIfOpen);

        [DllImport("WltRS.dll")]
        public static extern int GetBmp(string file_name, int intf);

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

        public static ReadCompletedEventArgs ReadIDCard()
        {
            try
            {
                LastError = null;
                Diagnostics = string.Empty;
                bool isUsbPort = false;
                int portNo = 0;
                int ret = 0;
                int pucIIN = 0;
                int pucSN = 0;
                int puiCHMsgLen = 0;
                int puiPHMsgLen = 0;
                int portID = 0;
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

                //下面找卡
                ret = SDT_StartFindIDCard(portID, ref pucIIN, isOpen);
                if (ret != 159)
                {

                    ret = SDT_StartFindIDCard(portID, ref pucIIN, isOpen);  //再找卡
                    if (ret != 159)
                    {
                        ret = SDT_ClosePort(portID);

                        LastError = $"找卡失败(返回值 {ret})：请将身份证放置在读卡器感应区";
                        return null;
                    }
                }

                //选卡
                ret = SDT_SelectIDCard(portID, ref pucSN, isOpen);
                if (ret != 144)
                {
                    ret = SDT_SelectIDCard(portID, ref pucSN, isOpen);  //再选卡
                    if (ret != 144)
                    {
                        ret = SDT_ClosePort(portID);

                        LastError = $"选卡失败(返回值 {ret})：请重新放置身份证";
                        return null;
                    }
                }



                IntPtr hCHMsg = Marshal.AllocHGlobal(1024);
                IntPtr hPHMsg = Marshal.AllocHGlobal(1024);

                StringBuilder ph = new StringBuilder(2048);
                ret = SDT_ReadBaseMsg(portID, hCHMsg, ref puiCHMsgLen, hPHMsg, ref puiPHMsgLen, isOpen);

                byte[] basicInfoBytes = new byte[puiCHMsgLen];
                Marshal.Copy(hCHMsg, basicInfoBytes, 0, puiCHMsgLen);
                Marshal.FreeHGlobal(hCHMsg);

                byte[] phData = new byte[puiPHMsgLen];
                Marshal.Copy(hPHMsg, phData, 0, puiPHMsgLen);
                Marshal.FreeHGlobal(hPHMsg);
                Diagnostics += $"ReadBaseMsg ret={ret}, chLen={puiCHMsgLen}, phLen={puiPHMsgLen}; ";

                if (ret != 144)
                {
                    ret = SDT_ClosePort(portID);
                    LastError = $"读卡失败(返回值 {ret})：请重新放置身份证";
                    return null;
                }



                ret = SDT_ClosePort(portID);



                string str = System.Text.UnicodeEncoding.Unicode.GetString(basicInfoBytes);
                cardInfo.Name = System.Text.UnicodeEncoding.Unicode.GetString(basicInfoBytes, 0, 30).Trim();
                cardInfo.GenderCode = System.Text.UnicodeEncoding.Unicode.GetString(basicInfoBytes, 30, 2).Trim();
                cardInfo.EthnicGroupCode = System.Text.UnicodeEncoding.Unicode.GetString(basicInfoBytes, 32, 4).Trim();
                string birthDate = System.Text.UnicodeEncoding.Unicode.GetString(basicInfoBytes, 36, 16).Trim();
                cardInfo.BirthDate = DateTime.ParseExact(birthDate, "yyyyMMdd", null);
                cardInfo.Address = System.Text.UnicodeEncoding.Unicode.GetString(basicInfoBytes, 52, 70).Trim();
                cardInfo.IDCard = System.Text.UnicodeEncoding.Unicode.GetString(basicInfoBytes, 122, 36).Trim();
                cardInfo.Issued = System.Text.UnicodeEncoding.Unicode.GetString(basicInfoBytes, 158, 30).Trim();
                string effectiveDate = System.Text.UnicodeEncoding.Unicode.GetString(basicInfoBytes, 188, basicInfoBytes.GetLength(0) - 188).Trim();
                cardInfo.StartDate = DateTime.ParseExact(effectiveDate.Substring(0, 8), "yyyyMMdd", null);
                var expiryTime = effectiveDate.Substring(8);
                if (expiryTime.Trim() != "长期")
                {
                    cardInfo.EndDate = DateTime.ParseExact(expiryTime, "yyyyMMdd", null); ;
                }
                else
                {
                    cardInfo.EndDate = DateTime.MaxValue;
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
                //ConstDefine.Logger.Error(ex);
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

        public string Name { get; set; }
        public string GenderCode{ get; set; }
  
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
        public string IDCard { get; set; }
        public string EthnicGroupCode { get; set; }

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
        public string Address { get; set; }
        public string Issued { get; set; }
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
        public byte[] PhotoData { get; set; }
    }
}
