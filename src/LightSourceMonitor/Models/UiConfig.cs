namespace LightSourceMonitor.Models;

/// <summary>界面偏好（config/UiConfig.json）。</summary>
public sealed class UiConfig
{
    /// <summary>界面语言（BCP 47）。仅当 <see cref="LanguageSetByUser"/> 为 true 时在启动时生效；否则启动时按操作系统 UI 语言匹配。</summary>
    public string Language { get; set; } = "";

    /// <summary>用户是否在设置中保存过语言。为 false 时首次启动根据操作系统 UI 语言匹配支持的语言，无法匹配则使用英文。</summary>
    public bool LanguageSetByUser { get; set; }
}
