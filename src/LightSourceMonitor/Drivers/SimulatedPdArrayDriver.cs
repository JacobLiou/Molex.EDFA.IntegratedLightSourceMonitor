using Microsoft.Extensions.Logging;

namespace LightSourceMonitor.Drivers;

public class SimulatedPdArrayDriver : IPdArrayDriver
{
    private const int ChannelCount = 8;
    private readonly ILogger<SimulatedPdArrayDriver> _logger;
    private readonly Random _rng = new();
    private readonly double[] _baselines = new double[ChannelCount];
    private readonly DateTime _startTime = DateTime.UtcNow;
    private bool _initialized;

    public bool IsOpen { get; private set; }
    public string DeviceSN { get; private set; } = "";

    public SimulatedPdArrayDriver(ILogger<SimulatedPdArrayDriver> logger)
    {
        _logger = logger;
    }

    public bool Open(string instanceIdSubstring)
    {
        DeviceSN = "SIM-PD-001";
        IsOpen = true;
        _logger.LogInformation("Simulated PD Array opened: {SN}", DeviceSN);
        return true;
    }

    public bool Initialize()
    {
        if (!IsOpen) return false;

        _baselines[0] = -8.5;
        _baselines[1] = -9.2;
        _baselines[2] = -10.1;
        _baselines[3] = -11.8;
        _baselines[4] = -10.5;
        _baselines[5] = -12.0;
        _baselines[6] = -13.3;
        _baselines[7] = -11.6;

        _initialized = true;
        _logger.LogInformation("Simulated PD Array initialized with {Count} channels", ChannelCount);
        return true;
    }

    public double[]? GetActualPower(int channelCount = 33)
    {
        if (!IsOpen || !_initialized) return null;

        int count = Math.Min(channelCount, ChannelCount);
        var powers = new double[count];
        double elapsed = (DateTime.UtcNow - _startTime).TotalSeconds;

        for (int i = 0; i < count; i++)
        {
            double drift = 0.05 * Math.Sin(2 * Math.PI * elapsed / 300.0 + i * 0.7);
            double noise = NextGaussian() * 0.02;
            double spike = _rng.NextDouble() < 0.01 ? (_rng.NextDouble() > 0.5 ? 0.3 : -0.3) : 0;

            powers[i] = Math.Round(_baselines[i] + drift + noise + spike, 3);
        }

        return powers;
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
