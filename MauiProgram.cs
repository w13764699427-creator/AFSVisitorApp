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

        // 设备能力 —— 摄像头 / 身份证读卡器 / OCR / 人脸比对
        builder.Services.AddSingleton<ICameraService, CameraService>();
        builder.Services.AddSingleton<IIdCardRecognitionService, MockIdCardRecognitionService>();
        builder.Services.AddSingleton<IIdCardReaderService, IdCardReaderService>();
        builder.Services.AddSingleton<IFaceCompareService, FaceCompareService>();

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
        // 超时不在此设置（启动即固化，设置页改了也不生效），由 ApiHelper 按每请求超时源控制；
        // BaseAddress 亦不依赖（ApiHelper 按当前 BaseUrl 动态拼址，支持运行时切换服务器）。
        builder.Services.AddSingleton(_ => new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan,
            DefaultRequestHeaders = { Accept = { new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json") } },
        });
        builder.Services.AddSingleton<ApiHelper>();

        // 真实后端（/KernelService）实现；离线演示时换回 MockVisitorRegistrationApi 即可，UI 与门面无需改动。
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
