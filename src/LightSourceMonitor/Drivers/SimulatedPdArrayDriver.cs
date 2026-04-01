using LightSourceMonitor.Models;
using LightSourceMonitor.Services.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightSourceMonitor.Drivers;

/// <summary>
/// Simulated PD array: power is generated around each channel's spec center from <see cref="IChannelCatalog"/>,
/// matching <see cref="Services.Alarm.AlarmService"/> (|power - center| vs AlarmDelta). Baseline noise is small; rare spikes exceed the threshold.
/// </summary>
public sealed class SimulatedPdArrayDriver : IPdArrayDriver
{
    private readonly ILogger<SimulatedPdArrayDriver> _logger;
    private readonly IChannelCatalog _channelCatalog;
    private readonly DriverSettings _driverSettings;
    private readonly Random _rng = new();
    private DateTime _startTime = DateTime.UtcNow;
    private bool _initialized;

    public bool IsOpen { get; private set; }
    public string DeviceSN { get; private set; } = "";

    public SimulatedPdArrayDriver(
        ILogger<SimulatedPdArrayDriver> logger,
        IChannelCatalog channelCatalog,
        IOptions<DriverSettings> driverOptions)
    {
        _logger = logger;
        _channelCatalog = channelCatalog;
        _driverSettings = driverOptions.Value;
    }

    public bool Open(string instanceIdSubstring)
    {
        var id = string.IsNullOrWhiteSpace(instanceIdSubstring) ? "SIM-PD-001" : instanceIdSubstring.Trim();
        var dev = _driverSettings.GetEffectiveDevices().FirstOrDefault(d => d.Enabled && (
            string.Equals(d.UsbAddress, id, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(d.DeviceSN, id, StringComparison.OrdinalIgnoreCase)));

        DeviceSN = dev?.DeviceSN ?? id;
        IsOpen = true;
        _startTime = DateTime.UtcNow;
        _logger.LogInformation("Simulated PD Array opened: catalog key {DeviceSN} (open id {OpenId})", DeviceSN, id);
        return true;
    }

    public bool Initialize()
    {
        if (!IsOpen) return false;

        _initialized = true;
        _logger.LogInformation("Simulated PD Array initialized: {SN}", DeviceSN);
        return true;
    }

    public double[]? GetActualPower(int channelCount = 33)
    {
        if (!IsOpen || !_initialized) return null;

        int count = Math.Max(1, channelCount);
        var powers = new double[count];
        double elapsed = (DateTime.UtcNow - _startTime).TotalSeconds;

        for (int i = 0; i < count; i++)
        {
            double center = GetSpecPowerCenterForChannel(i);
            double slowDrift = 0.018 * Math.Sin(2 * Math.PI * elapsed / 180.0 + i * 0.7);
            double fastDrift = 0.012 * Math.Sin(2 * Math.PI * elapsed / 30.0 + i * 1.3);
            double noise = NextGaussian() * 0.032;
            double spike = _rng.NextDouble() < 0.025
                ? (_rng.NextDouble() > 0.5 ? 1 : -1) * (0.18 + _rng.NextDouble() * 0.22)
                : 0;

            powers[i] = Math.Round(center + slowDrift + fastDrift + noise + spike, 3);
        }

        return powers;
    }

    private double GetSpecPowerCenterForChannel(int channelIndex)
    {
        var ch = _channelCatalog.GetEnabledChannels()
            .FirstOrDefault(c =>
                string.Equals(c.DeviceSN, DeviceSN, StringComparison.OrdinalIgnoreCase) &&
                c.ChannelIndex == channelIndex);

        if (ch != null)
            return (ch.SpecPowerMin + ch.SpecPowerMax) * 0.5;

        return -9.0 - (channelIndex % 8) * 0.35;
    }

    public WbaTelemetrySnapshot? GetWbaTelemetry()
    {
        if (!IsOpen || !_initialized) return null;

        double elapsed = (DateTime.UtcNow - _startTime).TotalSeconds;
        int deviceSeed = Math.Abs(DeviceSN.GetHashCode(StringComparison.OrdinalIgnoreCase)) % 1000;

        var voltages = new double[4];
        var temperatures = new double[4];

        for (int i = 0; i < 4; i++)
        {
            double phase = elapsed / 25.0 + i * 0.6 + deviceSeed * 0.001;
            voltages[i] = Math.Round(3.2 + i * 0.12 + 0.05 * Math.Sin(phase) + NextGaussian() * 0.01, 3);

            double tempPhase = elapsed / 40.0 + i * 0.8 + deviceSeed * 0.0015;
            temperatures[i] = Math.Round(31.0 + i * 0.9 + 0.45 * Math.Sin(tempPhase) + NextGaussian() * 0.08, 2);
        }

        double pressure = Math.Round(101.25 + 0.22 * Math.Sin(elapsed / 55.0 + deviceSeed * 0.0007) + NextGaussian() * 0.03, 3);

        return new WbaTelemetrySnapshot
        {
            DeviceSN = DeviceSN,
            Timestamp = DateTime.Now,
            Voltages = voltages,
            Temperatures = temperatures,
            AtmospherePressure = pressure
        };
    }

    public void Close()
    {
        IsOpen = false;
        _initialized = false;
        DeviceSN = "";
        _logger.LogInformation("Simulated PD Array closed");
    }

    public void Dispose()
    {
        Close();
        GC.SuppressFinalize(this);
    }

    private double NextGaussian()
    {
        double u1 = 1.0 - _rng.NextDouble();
        double u2 = _rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }
}
