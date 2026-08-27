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

        [DllImport("IDCard_Unpack.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int unpack(byte[] src, byte[] dst, int bmpSave);

        private static int isOpen = 1;               //自动开关串口 
        public static ReadCompletedEventArgs ReadIDCard()
        {
            try
            {
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

                if (ret != 144)
                {
                    ret = SDT_ClosePort(portID);
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
                ret = unpack(phData, output, 0);
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

                    using (var bitmap = new SKBitmap(102, 126, SKColorType.Rgb888x, SKAlphaType.Opaque))
                    {
                        IntPtr ptr = bitmap.GetPixels();
                        Marshal.Copy(output, 0, ptr, output.Length);
                        using (var image = SKImage.FromBitmap(bitmap))
                        using (var data = image.Encode(SKEncodedImageFormat.Jpeg, 90))
                        {
                            cardInfo.PhotoData = data.ToArray();
                        }
                    }
                }             
                return cardInfo;
            }
            catch (Exception ex)
            {
                //ConstDefine.Logger.Error(ex);
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
