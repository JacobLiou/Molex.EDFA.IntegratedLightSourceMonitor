using System.ComponentModel.DataAnnotations;

namespace LightSourceMonitor.Models;

/// <summary>WBA 遥测落库记录（与 <see cref="WbaTelemetrySnapshot"/> 对应）。</summary>
public sealed class WbaTelemetryRecord
{
    [Key]
    public long Id { get; set; }

    [MaxLength(128)]
    public string DeviceSN { get; set; } = "";

    public DateTime Timestamp { get; set; }

    /// <summary>JSON 数组，长度 4，对应电压。</summary>
    public string VoltagesJson { get; set; } = "[]";

    /// <summary>JSON 数组，长度 4，对应温度。</summary>
    public string TemperaturesJson { get; set; } = "[]";

    public double AtmospherePressure { get; set; }

    public bool IsUploadToTms { get; set; }
}
