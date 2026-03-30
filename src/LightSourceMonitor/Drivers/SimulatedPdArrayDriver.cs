using LightSourceMonitor.Models;
using Microsoft.Extensions.Logging;

namespace LightSourceMonitor.Drivers;

public class SimulatedPdArrayDriver : IPdArrayDriver
{
    private readonly ILogger<SimulatedPdArrayDriver> _logger;
    private readonly Random _rng = new();
    private readonly List<double> _baselines = new();
    private DateTime _startTime = DateTime.UtcNow;
    private bool _initialized;

    public bool IsOpen { get; private set; }
    public string DeviceSN { get; private set; } = "";

    public SimulatedPdArrayDriver(ILogger<SimulatedPdArrayDriver> logger)
    {
        _logger = logger;
    }

    public bool Open(string instanceIdSubstring)
    {
        DeviceSN = string.IsNullOrWhiteSpace(instanceIdSubstring) ? "SIM-PD-001" : instanceIdSubstring;
        IsOpen = true;
        _startTime = DateTime.UtcNow;
        _logger.LogInformation("Simulated PD Array opened: {SN}", DeviceSN);
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
        EnsureBaselineCount(count);

        var powers = new double[count];
        double elapsed = (DateTime.UtcNow - _startTime).TotalSeconds;

        for (int i = 0; i < count; i++)
        {
            double slowDrift = 0.08 * Math.Sin(2 * Math.PI * elapsed / 180.0 + i * 0.7);
            double fastDrift = 0.04 * Math.Sin(2 * Math.PI * elapsed / 30.0 + i * 1.3);
            double noise = NextGaussian() * 0.03;
            double spike = _rng.NextDouble() < 0.08
                ? (_rng.NextDouble() > 0.5 ? 1 : -1) * (0.2 + _rng.NextDouble() * 0.25)
                : 0;

            powers[i] = Math.Round(_baselines[i] + slowDrift + fastDrift + noise + spike, 3);
        }

        return powers;
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

    private void EnsureBaselineCount(int count)
    {
        while (_baselines.Count < count)
        {
            int idx = _baselines.Count;
            // Spread channels around -8 dBm to -14 dBm for realistic multi-channel simulation.
            _baselines.Add(-8.0 - (idx % 8) * 0.9 - (idx / 8) * 0.3);
        }
    }
}
