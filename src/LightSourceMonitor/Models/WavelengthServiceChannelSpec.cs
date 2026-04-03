namespace LightSourceMonitor.Models;

/// <summary>WM 波长服务表格一路的规格与告警容差（对应 appsettings.json WavelengthService.ChannelSpecs）。</summary>
public class WavelengthServiceChannelSpec
{
    /// <summary>与 QUERY 的通道下标一致，0..N-1。</summary>
    public int ChannelIndex { get; set; }

    public string ChannelName { get; set; } = "";

    /// <summary>标称波长 (nm)。≤0 表示不参与波长告警。</summary>
    public double SpecWavelengthNm { get; set; }

    /// <summary>允许偏差 (nm)。0 表示使用 <see cref="WavelengthServiceSettings.DefaultWavelengthAlarmDeltaNm"/>。</summary>
    public double AlarmDeltaNm { get; set; }

    public bool IsEnabled { get; set; } = true;
}
