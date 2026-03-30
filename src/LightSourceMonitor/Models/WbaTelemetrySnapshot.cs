namespace LightSourceMonitor.Models;

public sealed class WbaTelemetrySnapshot
{
    public string DeviceSN { get; init; } = "";
    public DateTime Timestamp { get; init; }
    public double[] Voltages { get; init; } = new double[4];
    public double[] Temperatures { get; init; } = new double[4];
    public double AtmospherePressure { get; init; }
}
