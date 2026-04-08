namespace LightSourceMonitor.Services.Localization;

public interface ILanguageService
{
    /// <summary>当前语言名称，如 zh-CN、en-US。</summary>
    string CurrentLanguage { get; }

    event EventHandler? LanguageChanged;

    /// <summary>切换界面语言并合并资源字典；未知值回退为 zh-CN。</summary>
    void ApplyLanguage(string cultureName);

    /// <summary>从当前语言字典取字符串；缺失时返回 <paramref name="key"/>。</summary>
    string GetString(string key);
}
