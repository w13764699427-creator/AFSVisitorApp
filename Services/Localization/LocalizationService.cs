using System.Globalization;
using System.Resources;

namespace VisitorApp.Services.Localization;

public record LanguageOption(string Code, string NativeName, string EnglishName, string Flag);

/// <summary>
/// 标准多语言服务 — 基于 .resx 资源文件 + ResourceManager。
/// 资源文件位于 Resources/AppResources.resx（默认/zh-CN）、
/// Resources/AppResources.en-US.resx、Resources/AppResources.zh-TW.resx。
/// 切换语言会广播 OnLanguageChanged 事件，组件可订阅后 StateHasChanged。
/// 用户偏好通过 Preferences 持久化（Key=visitorapp.language）。
/// </summary>
public class LocalizationService
{
    private const string PreferenceKey = "visitorapp.language";
    private static readonly ResourceManager ResourceManager =
        new("VisitorApp.Resources.AppResources", typeof(LocalizationService).Assembly);

    public IReadOnlyList<LanguageOption> AvailableLanguages { get; } = new[]
    {
        new LanguageOption("zh-CN", "简体中文", "Simplified Chinese",  "🇨🇳"),
        new LanguageOption("en-US", "English",  "English",             "🇺🇸"),
        new LanguageOption("zh-TW", "繁體中文", "Traditional Chinese",  "🇹🇼"),
    };

    public string CurrentLanguage { get; private set; } = "zh-CN";

    public LanguageOption CurrentOption =>
        AvailableLanguages.FirstOrDefault(l => l.Code == CurrentLanguage) ?? AvailableLanguages[0];

    public event Action? OnLanguageChanged;

    public LocalizationService()
    {
        var saved = Preferences.Default.Get(PreferenceKey, "zh-CN");
        ApplyLanguage(saved);
    }

    /// <summary>索引器：L["key"] 获取翻译文本。</summary>
    public string this[string key] => Translate(key);

    /// <summary>获取翻译文本，缺失时回退到 key 本身。</summary>
    public string Translate(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;

        try
        {
            var culture = new CultureInfo(CurrentLanguage);
            var value = ResourceManager.GetString(key, culture);
            if (!string.IsNullOrEmpty(value))
                return value;
        }
        catch (CultureNotFoundException) { }

        // 回退到默认资源（zh-CN）
        var fallback = ResourceManager.GetString(key, CultureInfo.InvariantCulture);
        return fallback ?? key;
    }

    /// <summary>格式化翻译：L.Format("key", arg0, arg1, …)。</summary>
    public string Format(string key, params object?[] args)
    {
        try
        {
            return string.Format(Translate(key), args);
        }
        catch (FormatException)
        {
            return Translate(key);
        }
    }

    /// <summary>切换语言。</summary>
    public void SetLanguage(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code == CurrentLanguage) return;
        if (!AvailableLanguages.Any(l => l.Code == code)) return;
        ApplyLanguage(code);
        Preferences.Default.Set(PreferenceKey, code);
        OnLanguageChanged?.Invoke();
    }

    private void ApplyLanguage(string code)
    {
        CurrentLanguage = code;
        try
        {
            var culture = new CultureInfo(code);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }
        catch (CultureNotFoundException)
        {
            // ignore，保留默认
        }
    }
}
