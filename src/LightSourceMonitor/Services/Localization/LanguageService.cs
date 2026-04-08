using System.Globalization;
using System.Windows;
using Microsoft.Extensions.Logging;

namespace LightSourceMonitor.Services.Localization;

public sealed class LanguageService : ILanguageService
{
    public const string ZhCn = "zh-CN";
    public const string EnUs = "en-US";
    public const string JaJp = "ja-JP";
    public const string MsMy = "ms-MY";
    public const string ViVn = "vi-VN";

    /// <summary>与 <c>Strings.&lt;culture&gt;.xaml</c> 文件名一致的可选 UI 语言。</summary>
    public static readonly IReadOnlyList<string> SupportedLanguages =
        [ZhCn, EnUs, JaJp, MsMy, ViVn];

    private static readonly string[] Supported = [ZhCn, EnUs, JaJp, MsMy, ViVn];

    private readonly ILogger<LanguageService> _logger;
    private ResourceDictionary? _languageDictionary;
    private string _currentLanguage = ZhCn;

    public LanguageService(ILogger<LanguageService> logger)
    {
        _logger = logger;
    }

    public string CurrentLanguage => _currentLanguage;

    public event EventHandler? LanguageChanged;

    public void ApplyLanguage(string cultureName)
    {
        var name = NormalizeCulture(cultureName);
        if (name == _currentLanguage && _languageDictionary != null)
            return;

        var app = Application.Current;
        if (app == null) return;

        var source = new Uri(
            $"/LightSourceMonitor;component/Resources/Strings.{name}.xaml",
            UriKind.Relative);

        ResourceDictionary next;
        try
        {
            next = new ResourceDictionary { Source = source };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load language dictionary for {Culture}", name);
            if (name != ZhCn)
            {
                ApplyLanguage(ZhCn);
                return;
            }

            throw;
        }

        if (_languageDictionary != null)
            app.Resources.MergedDictionaries.Remove(_languageDictionary);

        _languageDictionary = next;
        app.Resources.MergedDictionaries.Insert(0, _languageDictionary);
        _currentLanguage = name;

        var culture = CultureInfo.GetCultureInfo(name);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;

        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string GetString(string key)
    {
        if (Application.Current?.TryFindResource(key) is string s)
            return s;
        return key;
    }

    private static string NormalizeCulture(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName))
            return ZhCn;

        var c = cultureName.Trim();
        foreach (var x in Supported)
        {
            if (string.Equals(x, c, StringComparison.OrdinalIgnoreCase))
                return x;
        }

        return ZhCn;
    }
}
