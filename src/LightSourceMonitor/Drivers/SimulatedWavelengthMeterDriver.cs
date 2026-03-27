using Microsoft.Extensions.Logging;

namespace LightSourceMonitor.Drivers;

public class SimulatedWavelengthMeterDriver : IWavelengthMeterDriver
{
    private readonly ILogger<SimulatedWavelengthMeterDriver> _logger;
    private readonly Random _rng = new();
    private readonly DateTime _startTime = DateTime.UtcNow;

    private readonly Dictionary<int, ChannelParams> _params = new();
    private readonly HashSet<int> _sweptChannels = new();

    public bool IsInitialized { get; private set; }

    public SimulatedWavelengthMeterDriver(ILogger<SimulatedWavelengthMeterDriver> logger)
    {
        _logger = logger;
    }

    public bool Init(string configXmlPath)
    {
        IsInitialized = true;
        _logger.LogInformation("Simulated WavelengthMeter initialized (config ignored: {Path})", configXmlPath);
        return true;
    }

    public bool SetParameters(int chIndex, int wlUnit, double startWl, double stopWl,
                              double peakExcursion, double peakThreshold)
    {
        if (!IsInitialized) return false;
        _params[chIndex] = new ChannelParams
        {
            CenterWl = (startWl + stopWl) / 2.0,
            StartWl = startWl,
            StopWl = stopWl,
            PeakExcursion = peakExcursion,
            PeakThreshold = peakThreshold
        };
        return true;
    }

    public bool ExecuteSingleSweep(int chIndex)
    {
        if (!IsInitialized) return false;
        _sweptChannels.Add(chIndex);
        return true;
    }

    public (double[] wavelengths, double[] powers, int count)? GetResult(int chIndex)
    {
        if (!IsInitialized || !_sweptChannels.Contains(chIndex)) return null;
        _sweptChannels.Remove(chIndex);

        double centerWl = _params.TryGetValue(chIndex, out var p) ? p.CenterWl : 1310.0;
        double elapsed = (DateTime.UtcNow - _startTime).TotalSeconds;

        double drift = 0.002 * Math.Sin(2 * Math.PI * elapsed / 600.0 + chIndex * 1.1);
        double noise = NextGaussian() * 0.003;
        double spike = _rng.NextDouble() < 0.005 ? (_rng.NextDouble() > 0.5 ? 0.02 : -0.02) : 0;

        double wl = Math.Round(centerWl + drift + noise + spike, 4);
        double power = Math.Round(-10.0 + NextGaussian() * 0.5, 2);

        return (new[] { wl }, new[] { power }, 1);
    }

    public void Close()
    {
        IsInitialized = false;
        _params.Clear();
        _sweptChannels.Clear();
        _logger.LogInformation("Simulated WavelengthMeter closed");
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

    private class ChannelParams
    {
        public double CenterWl;
        public double StartWl;
        public double StopWl;
        public double PeakExcursion;
        public double PeakThreshold;
    }
}
