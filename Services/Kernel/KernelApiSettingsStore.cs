namespace VisitorApp.Services.Kernel;

/// <summary>
/// 后端连接配置（<see cref="KernelApiOptions"/>）的本地持久化读写。
/// 通过 MAUI <c>Preferences</c> 保存现场部署参数（服务器地址 / 服务账号等），
/// 应用启动时回填到单例 <see cref="KernelApiOptions"/>，修改后即时生效（参见 ApiHelper 动态拼址）。
/// </summary>
public class KernelApiSettingsStore
{
    private const string KeyBaseUrl = "kernel.baseurl";
    private const string KeyServiceAccount = "kernel.account";
    private const string KeyServicePassword = "kernel.password";
    private const string KeyClientId = "kernel.clientid";
    private const string KeyKeepTime = "kernel.keeptime";
    private const string KeyTimeout = "kernel.timeout";
    private const string KeyShiftBeijing = "kernel.shiftbeijing";

    /// <summary>是否已存在用户保存过的配置。</summary>
    public bool HasSaved => Preferences.Default.ContainsKey(KeyBaseUrl);

    /// <summary>把本地保存的配置回填到 <paramref name="options"/>（缺省项保留其原有默认值）。</summary>
    public void Load(KernelApiOptions options)
    {
        options.BaseUrl = Preferences.Default.Get(KeyBaseUrl, options.BaseUrl);
        options.ServiceAccount = Preferences.Default.Get(KeyServiceAccount, options.ServiceAccount);
        options.ServicePassword = Preferences.Default.Get(KeyServicePassword, options.ServicePassword);
        options.ClientId = Preferences.Default.Get(KeyClientId, options.ClientId);
        options.KeepTimeSeconds = Preferences.Default.Get(KeyKeepTime, options.KeepTimeSeconds);
        options.TimeoutSeconds = Preferences.Default.Get(KeyTimeout, options.TimeoutSeconds);
        options.ShiftToBeijingTimeOnWrite = Preferences.Default.Get(KeyShiftBeijing, options.ShiftToBeijingTimeOnWrite);
    }

    /// <summary>持久化当前 <paramref name="options"/> 到本地存储。</summary>
    public void Save(KernelApiOptions options)
    {
        Preferences.Default.Set(KeyBaseUrl, NormalizeBaseUrl(options.BaseUrl));
        Preferences.Default.Set(KeyServiceAccount, options.ServiceAccount ?? string.Empty);
        Preferences.Default.Set(KeyServicePassword, options.ServicePassword ?? string.Empty);
        Preferences.Default.Set(KeyClientId, options.ClientId ?? string.Empty);
        Preferences.Default.Set(KeyKeepTime, options.KeepTimeSeconds);
        Preferences.Default.Set(KeyTimeout, options.TimeoutSeconds);
        Preferences.Default.Set(KeyShiftBeijing, options.ShiftToBeijingTimeOnWrite);
    }

    /// <summary>清除本地保存的配置（下次启动恢复代码内置默认值）。</summary>
    public void Clear()
    {
        foreach (var key in new[]
                 {
                     KeyBaseUrl, KeyServiceAccount, KeyServicePassword,
                     KeyClientId, KeyKeepTime, KeyTimeout, KeyShiftBeijing,
                 })
        {
            Preferences.Default.Remove(key);
        }
    }

    /// <summary>统一服务器地址：去除首尾空白与末尾斜杠，便于后续与相对路径拼接。</summary>
    public static string NormalizeBaseUrl(string? url)
        => (url ?? string.Empty).Trim().TrimEnd('/');
}
