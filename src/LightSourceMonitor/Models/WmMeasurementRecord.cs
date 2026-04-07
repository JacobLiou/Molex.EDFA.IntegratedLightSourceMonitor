using System.ComponentModel.DataAnnotations;

namespace LightSourceMonitor.Models;

/// <summary>WM 波长服务表格一路在一次 sweep 中的实测落库记录（用于趋势）。</summary>
public class WmMeasurementRecord
{
    [Key]
    public long Id { get; set; }

    public DateTime Timestamp { get; set; }

    /// <summary>QUERY 设备参数，与 <see cref="WavelengthServiceSettings.QueryDeviceId"/> 一致。</summary>
    public string QueryDeviceId { get; set; } = "";

    public int ChannelIndex { get; set; }

    /// <summary>实测波长 (nm)；无效读数时可填 0。</summary>
    public double WavelengthNm { get; set; }

    /// <summary>WM 返回的功率 (dBm)，无效时为 null。</summary>
    public double? WmPowerDbm { get; set; }

    public bool IsValid { get; set; }
}
