using System.ComponentModel.DataAnnotations;

namespace LightSourceMonitor.Models;

/// <summary>WBA 设备一次遥测落库（与 <see cref="WbaTelemetrySnapshot"/> 字段对齐）。</summary>
public class WbaMeasurementRecord
{
    [Key]
    public long Id { get; set; }

    public DateTime Timestamp { get; set; }

    public string DeviceSN { get; set; } = "";

    public double Temperature0 { get; set; }
    public double Temperature1 { get; set; }
    public double Temperature2 { get; set; }
    public double Temperature3 { get; set; }

    public double Voltage0 { get; set; }
    public double Voltage1 { get; set; }
    public double Voltage2 { get; set; }
    public double Voltage3 { get; set; }

    public double AtmospherePressure { get; set; }
}
