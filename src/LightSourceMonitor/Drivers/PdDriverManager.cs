using LightSourceMonitor.Models;
using Microsoft.Extensions.Logging;

namespace LightSourceMonitor.Drivers;

public interface IPdDriverManager : IDisposable
{
    IReadOnlyDictionary<string, bool> ConnectionStates { get; }
    bool AnyConnected { get; }
    void ConfigureDevices(IEnumerable<PdDeviceSettings> devices);
    bool TryReadPower(string deviceSn, int channelCount, out double[]? powers);
    bool TryReconnect(string deviceSn);
    void CloseAll();
}

public class PdDriverManager : IPdDriverManager
{
    private readonly Func<IPdArrayDriver> _driverFactory;
    private readonly ILogger<PdDriverManager> _logger;
    private readonly Dictionary<string, DeviceContext> _devices = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public IReadOnlyDictionary<string, bool> ConnectionStates => _devices
        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Connected, StringComparer.OrdinalIgnoreCase);

    public bool AnyConnected => _devices.Values.Any(v => v.Connected);

    public PdDriverManager(Func<IPdArrayDriver> driverFactory, ILogger<PdDriverManager> logger)
    {
        _driverFactory = driverFactory;
        _logger = logger;
    }

    public void ConfigureDevices(IEnumerable<PdDeviceSettings> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);

        CloseAll();
        _devices.Clear();

        foreach (var device in devices.Where(d => d.Enabled))
        {
            if (string.IsNullOrWhiteSpace(device.DeviceSN) || string.IsNullOrWhiteSpace(device.UsbAddress))
                continue;

            if (_devices.ContainsKey(device.DeviceSN))
            {
                _logger.LogWarning("Duplicate DeviceSN in config: {DeviceSN}", device.DeviceSN);
                continue;
            }

            _devices[device.DeviceSN] = new DeviceContext
            {
                Settings = device,
                Driver = _driverFactory()
            };
        }

        _logger.LogInformation("Configured PD devices: {Count}", _devices.Count);
    }

    public bool TryReadPower(string deviceSn, int channelCount, out double[]? powers)
    {
        powers = null;
        if (!_devices.TryGetValue(deviceSn, out var ctx))
            return false;

        if (!ctx.Connected && !TryOpenAndInit(ctx))
            return false;

        powers = ctx.Driver.GetActualPower(channelCount);
        if (powers == null)
        {
            ctx.Connected = false;
            return false;
        }

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
                ctx.Driver.Close();
                ctx.Driver.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error closing PD driver for {DeviceSN}", ctx.Settings.DeviceSN);
            }

            ctx.Connected = false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        CloseAll();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private bool TryOpenAndInit(DeviceContext ctx)
    {
        try
        {
            if (!ctx.Driver.IsOpen && !ctx.Driver.Open(ctx.Settings.UsbAddress))
            {
                ctx.Connected = false;
                return false;
            }

            if (!ctx.Driver.Initialize())
            {
                ctx.Connected = false;
                return false;
            }

            ctx.Connected = true;
            return true;
        }
        catch (Exception ex)
        {
            ctx.Connected = false;
            _logger.LogWarning(ex, "PD connect/init failed for {DeviceSN} ({UsbAddress})", ctx.Settings.DeviceSN, ctx.Settings.UsbAddress);
            return false;
        }
    }

    private sealed class DeviceContext
    {
        public required PdDeviceSettings Settings { get; init; }
        public required IPdArrayDriver Driver { get; init; }
        public bool Connected { get; set; }
    }
}