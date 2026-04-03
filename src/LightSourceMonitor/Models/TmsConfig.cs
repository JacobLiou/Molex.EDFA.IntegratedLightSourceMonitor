namespace LightSourceMonitor.Models;

/// <summary>TMS 上报配置，对应 <c>config/TmsConfig.json</c>。</summary>
public class TmsConfig
{
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public int UploadIntervalSec { get; set; } = 300;
    public bool IsEnabled { get; set; }
}
