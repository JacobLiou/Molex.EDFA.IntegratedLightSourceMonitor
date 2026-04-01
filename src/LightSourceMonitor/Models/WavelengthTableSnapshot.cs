namespace LightSourceMonitor.Models;

public sealed class WavelengthTableSnapshot
{
    public required string QueryDeviceId { get; init; }
    public DateTime Timestamp { get; init; }
    public required IReadOnlyList<WavelengthTableRow> Rows { get; init; }
}

public sealed class WavelengthTableRow
{
    public int ChannelIndex { get; init; }
    public double WavelengthNm { get; init; }
    public double WmPowerDbm { get; init; }
    public bool IsValid { get; init; }
}
