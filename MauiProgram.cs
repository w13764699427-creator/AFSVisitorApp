using Microsoft.Extensions.Logging;
using VisitorApp.Services;
using VisitorApp.Services.Kernel;
using VisitorApp.Services.Localization;
using VisitorApp.Services.MockApi;

namespace VisitorApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddAntDesign();

        // 数据层
        builder.Services.AddSingleton<VisitorDatabase>();

        // 业务状态
        builder.Services.AddScoped<VisitorFormState>();

        // 设备能力 —— 摄像头 / 身份证读卡器 / OCR
        builder.Services.AddSingleton<ICameraService, CameraService>();
        builder.Services.AddSingleton<IIdCardRecognitionService, MockIdCardRecognitionService>();
        builder.Services.AddSingleton<IIdCardReaderService, IdCardReaderService>();

        // 仿真后端：统计 / 员工目录 / 提交回执 / 审批查询
        builder.Services.AddSingleton<IMockApiService, MockApiService>();

        // ===== 真实后端（/KernelService）对接：基于 VisitorRegistrationSheetInfo + Operation 契约 =====
        // 现场连接参数（服务器 IP 等）可在「设置」页配置并本地持久化；启动时回填到默认值之上。
        var kernelOptions = new KernelApiOptions();
        var kernelSettings = new KernelApiSettingsStore();
        kernelSettings.Load(kernelOptions);
        builder.Services.AddSingleton(kernelOptions);
        builder.Services.AddSingleton(kernelSettings);

        // 供真实实现使用的 HttpClient（Mock 模式下创建但不会发起请求）。
        // Accept: application/json —— 后端为 WCF 风格，不带该头会默认回 XML。
        builder.Services.AddSingleton(_ => new HttpClient
        {
            BaseAddress = new Uri(kernelOptions.BaseUrl),
            Timeout = TimeSpan.FromSeconds(kernelOptions.TimeoutSeconds),
            DefaultRequestHeaders = { Accept = { new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json") } },
        });
        builder.Services.AddSingleton<ApiHelper>();

        // 默认走离线 Mock，保证无后端也能完整演示"预约→签到→查询→签退"。
        // 接入真实后端：把下面这行替换为 KernelVisitorRegistrationApi 即可，UI 与门面无需改动。
        //builder.Services.AddSingleton<IVisitorRegistrationApi, MockVisitorRegistrationApi>();
        builder.Services.AddSingleton<IVisitorRegistrationApi, KernelVisitorRegistrationApi>();

        builder.Services.AddSingleton<VisitorRegistrationService>();

        // 多语言：zh-CN / en-US / zh-TW
        builder.Services.AddSingleton<LocalizationService>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
