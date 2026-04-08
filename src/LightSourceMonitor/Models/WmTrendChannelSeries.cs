namespace LightSourceMonitor.Models;

/// <summary>波长计趋势中一路的时间序列（已按时间排序，可能已降采样）。</summary>
public sealed class WmTrendChannelSeries
{
    public required int ChannelIndex { get; init; }
    public required string SeriesName { get; init; }
    public required IReadOnlyList<(DateTime Timestamp, double WavelengthNm)> Points { get; init; }
}
