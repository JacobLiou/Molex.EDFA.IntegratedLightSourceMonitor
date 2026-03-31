using LightSourceMonitor.Models;
using Microsoft.Extensions.Logging;

namespace LightSourceMonitor.Drivers;

public interface IWbaDeviceManager : IDisposable
{
    IReadOnlyDictionary<string, bool> ConnectionStates { get; }
    bool AnyConnected { get; }
    void ConfigureDevices(IEnumerable<WbaDeviceSettings> devices);
    bool TryReadTelemetry(string deviceSn, out WbaTelemetrySnapshot? telemetry);
    bool TryReconnect(string deviceSn);
    void CloseAll();
}

public class WbaDeviceManager : IWbaDeviceManager
{
    private readonly ILogger<WbaDeviceManager> _logger;
    private readonly WbaDriver _wbaDriver;
    private readonly Dictionary<string, WbaDeviceContext> _devices = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public IReadOnlyDictionary<string, bool> ConnectionStates =>
        _devices.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Connected, StringComparer.OrdinalIgnoreCase);

    public bool AnyConnected => _devices.Values.Any(v => v.Connected);

    public WbaDeviceManager(WbaDriver wbaDriver, ILogger<WbaDeviceManager> logger)
    {
        _wbaDriver = wbaDriver;
        _logger = logger;
    }

    public void ConfigureDevices(IEnumerable<WbaDeviceSettings> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        CloseAll();
        _devices.Clear();

        foreach (var device in devices.Where(d => d.Enabled))
        {
            if (string.IsNullOrWhiteSpace(device.DeviceSN) || string.IsNullOrWhiteSpace(device.ComPort))
                continue;

            if (_devices.ContainsKey(device.DeviceSN))
            {
                _logger.LogWarning("Duplicate WBA DeviceSN: {DeviceSN}", device.DeviceSN);
                continue;
            }

            _devices[device.DeviceSN] = new WbaDeviceContext
            {
                Settings = device,
                Connected = false
            };
        }

        _logger.LogInformation("Configured WBA devices: {Count}", _devices.Count);
    }

    public bool TryReadTelemetry(string deviceSn, out WbaTelemetrySnapshot? telemetry)
    {
        telemetry = null;
        if (!_devices.TryGetValue(deviceSn, out var ctx))
            return false;

        if (!ctx.Connected && !TryOpenAndInit(ctx))
            return false;

        telemetry = _wbaDriver.ReadTelemetry(deviceSn);
        return true;
    }

    public bool TryReconnect(string deviceSn)
    {
        if (!_devices.TryGetValue(deviceSn, out var ctx))
            return false;

        if (ctx.Connected)
            return true;

        return TryOpenAndInit(ctx);
    }

    public void CloseAll()
    {
        foreach (var ctx in _devices.Values)
        {
            try
            {
                _wbaDriver.Close();
                ctx.Connected = false;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error closing WBA for {DeviceSN}", ctx.Settings.DeviceSN);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        CloseAll();
        _wbaDriver.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private bool TryOpenAndInit(WbaDeviceContext ctx)
    {
        try
        {
            if (!_wbaDriver.Open(ctx.Settings.ComPort))
            {
                ctx.Connected = false;
                return false;
            }

            ctx.Connected = true;
            _logger.LogInformation("WBA initialized: {DeviceSN} on {ComPort}", ctx.Settings.DeviceSN, ctx.Settings.ComPort);
            return true;
        }
        catch (Exception ex)
        {
            ctx.Connected = false;
            _logger.LogWarning(ex, "WBA init failed for {DeviceSN}", ctx.Settings.DeviceSN);
            return false;
        }
    }

    private sealed class WbaDeviceContext
    {
        public required WbaDeviceSettings Settings { get; init; }
        public bool Connected { get; set; }
    }
}
