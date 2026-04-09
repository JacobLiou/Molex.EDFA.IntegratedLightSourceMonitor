namespace LightSourceMonitor.Models;

/// <summary>WBA 趋势中一条曲线（通常按设备 SN）。</summary>
public sealed class WbaTrendSeries
{
    public required string SeriesName { get; init; }
    public required IReadOnlyList<(DateTime Timestamp, double Value)> Points { get; init; }
}
