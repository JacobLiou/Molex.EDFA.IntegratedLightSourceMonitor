using LightSourceMonitor.Models;

namespace LightSourceMonitor.Drivers;

public class SimulatedWbaDeviceManager : IWbaDeviceManager
{
    private readonly Random _rng = new();
    private readonly Dictionary<string, WbaDeviceSettings> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly DateTime _startTime = DateTime.UtcNow;

    public IReadOnlyDictionary<string, bool> ConnectionStates =>
        _devices.ToDictionary(kvp => kvp.Key, _ => true, StringComparer.OrdinalIgnoreCase);

    public bool AnyConnected => _devices.Count > 0;

    public void ConfigureDevices(IEnumerable<WbaDeviceSettings> devices)
    {
        _devices.Clear();
        foreach (var d in devices.Where(d => d.Enabled && !string.IsNullOrWhiteSpace(d.DeviceSN)))
            _devices[d.DeviceSN] = d;
    }

    public bool TryReadTelemetry(string deviceSn, out WbaTelemetrySnapshot? telemetry)
    {
        telemetry = null;
        if (!_devices.ContainsKey(deviceSn)) return false;

        double elapsed = (DateTime.UtcNow - _startTime).TotalSeconds;
        int seed = Math.Abs(deviceSn.GetHashCode()) % 1000;

        var temps = new double[4];
        var volts = new double[4];
        for (int i = 0; i < 4; i++)
        {
            double tPhase = elapsed / 40.0 + i * 0.8 + seed * 0.0015;
            temps[i] = Math.Round(31.0 + i * 0.9 + 0.45 * Math.Sin(tPhase) + NextGaussian() * 0.08, 2);

            double vPhase = elapsed / 25.0 + i * 0.6 + seed * 0.001;
            volts[i] = Math.Round(3.2 + i * 0.12 + 0.05 * Math.Sin(vPhase) + NextGaussian() * 0.01, 3);
        }

        double pressure = Math.Round(101.25 + 0.22 * Math.Sin(elapsed / 55.0 + seed * 0.0007) + NextGaussian() * 0.03, 3);

        telemetry = new WbaTelemetrySnapshot
        {
            DeviceSN = deviceSn,
            Timestamp = DateTime.Now,
            Temperatures = temps,
            Voltages = volts,
            AtmospherePressure = pressure
        };
        return true;
    }

    public bool TryReconnect(string deviceSn) => _devices.ContainsKey(deviceSn);

    public void CloseAll() { }

    public void Dispose() { GC.SuppressFinalize(this); }

    private double NextGaussian()
    {
        double u1 = 1.0 - _rng.NextDouble();
        double u2 = 1.0 - _rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }
}
