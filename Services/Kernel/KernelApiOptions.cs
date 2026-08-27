namespace VisitorApp.Services.Kernel;

/// <summary>
/// 真实后端（/KernelService）连接与服务账号配置。
/// 自助机以一个固定服务账号登录换取 UserToken，再以该 Token 调用各接口。
/// 部署到不同现场时只需调整这里（或从配置文件 / 环境变量注入）。
/// </summary>
public class KernelApiOptions
{
    /// <summary>后端根地址，例如 http://10.0.0.8:8088 。末尾不要带斜杠。</summary>
    public string BaseUrl { get; set; } = "http://localhost:8088";

    /// <summary>服务账号用户名。</summary>
    public string ServiceAccount { get; set; } = "admin";

    /// <summary>服务账号密码。</summary>
    public string ServicePassword { get; set; } = "admin";

    /// <summary>客户端标识（多端会话区分）。</summary>
    public string ClientId { get; set; } = "AFS-VISITOR-KIOSK";

    /// <summary>会话保持时长（秒）。</summary>
    public int KeepTimeSeconds { get; set; } = 7200;

    /// <summary>HTTP 超时（秒）。</summary>
    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>
    /// 服务端是否以东八区(本地)无时区时间存储。
    /// 为 true 时提交前对 DateTime 统一 +8 小时、读取后转本地，复刻原 WPF Operation 的处理。
    /// </summary>
    public bool ShiftToBeijingTimeOnWrite { get; set; } = true;
}
