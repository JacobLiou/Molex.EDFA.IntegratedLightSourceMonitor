namespace LightSourceMonitor.Models;

/// <summary>TMS 上报配置，对应 <c>config/TmsConfig.json</c>。</summary>
public class TmsConfig
{
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public int UploadIntervalSec { get; set; } = 300;
    public bool IsEnabled { get; set; }

    /// <summary>PD 测量数据 POST 路径（相对 BaseUrl）。</summary>
    public string MeasurementsPath { get; set; } = "/measurements";

    /// <summary>波长计快照 POST 路径（相对 BaseUrl）。</summary>
    public string WavelengthSnapshotsPath { get; set; } = "/wavelength-snapshots";

    /// <summary>WBA 遥测 POST 路径（相对 BaseUrl）。</summary>
    public string WbaTelemetryPath { get; set; } = "/wba-telemetry";
}
