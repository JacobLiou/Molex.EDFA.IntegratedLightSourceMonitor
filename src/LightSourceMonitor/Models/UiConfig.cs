namespace LightSourceMonitor.Models;

/// <summary>界面偏好（config/UiConfig.json）。</summary>
public sealed class UiConfig
{
    /// <summary>UI 区域性与语言包，如 zh-CN、en-US。</summary>
    public string Language { get; set; } = "zh-CN";
}
