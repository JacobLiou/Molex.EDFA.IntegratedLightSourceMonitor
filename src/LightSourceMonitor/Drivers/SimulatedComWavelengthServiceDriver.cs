using LightSourceMonitor.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightSourceMonitor.Drivers;

/// <summary>
/// Simulated wavelength meter driver that emulates COM-port style query/response.
/// </summary>
public class SimulatedComWavelengthServiceDriver : IWavelengthServiceDriver
{
    private readonly ILogger<SimulatedComWavelengthServiceDriver> _logger;
    private readonly WavelengthServiceSettings _settings;
    private readonly object _sync = new();
    private readonly Random _random = new();
    private readonly DateTime _start = DateTime.UtcNow;

    private bool _disposed;

    public bool IsConnected { get; private set; }

    public SimulatedComWavelengthServiceDriver(
        ILogger<SimulatedComWavelengthServiceDriver> logger,
        IOptions<WavelengthServiceSettings> options)
    {
        _logger = logger;
        _settings = options.Value;
    }

    public Task<bool> ConnectAsync(string host, int port)
    {
        if (_disposed)
            return Task.FromResult(false);

        IsConnected = true;
        _logger.LogInformation(
            "Simulated COM wavelength driver connected: {ComPort}@{BaudRate}",
            _settings.ComPort,
            _settings.BaudRate);

        return Task.FromResult(true);
    }

    public Task<bool> DisconnectAsync()
    {
        IsConnected = false;
        _logger.LogInformation("Simulated COM wavelength driver disconnected");
        return Task.FromResult(true);
    }

    public async Task<(double wavelength, double power)> GetWavelengthAsync(string deviceId, int channelIndex)
    {
        if (_settings.SimulatedComReadDelayMs > 0)
            await Task.Delay(_settings.SimulatedComReadDelayMs);

        if (!IsConnected)
        {
            _logger.LogDebug("Simulated COM driver queried while disconnected; auto-generating sample");
        }

        var request = $"READ {deviceId} CH{channelIndex + 1}";
        var response = BuildResponse(request, channelIndex);

        if (TryParseResponse(response, out var wl, out var pwr))
            return (wl, pwr);

        // Fallback should never happen; keep deterministic safe data.
        return (0, 0);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        IsConnected = false;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private string BuildResponse(string request, int channelIndex)
    {
        lock (_sync)
        {
            var elapsed = (DateTime.UtcNow - _start).TotalSeconds;
            var baseWl = _settings.SimulatedWavelengthMin + channelIndex * _settings.SimulatedChannelSpacingNm;
            var drift = 0.01 * Math.Sin(elapsed / 12.0 + channelIndex * 0.7);
            var noiseWl = (_random.NextDouble() * 2 - 1) * _settings.SimulatedComNoiseNm;

            var wl = baseWl + drift + noiseWl;
            if (wl < _settings.SimulatedWavelengthMin)
                wl = _settings.SimulatedWavelengthMin;
            if (wl > _settings.SimulatedWavelengthMax)
                wl = _settings.SimulatedWavelengthMax;

            var centerPower = (_settings.SimulatedPowerMin + _settings.SimulatedPowerMax) / 2.0;
            var swing = (_settings.SimulatedPowerMax - _settings.SimulatedPowerMin) / 2.0;
            var shape = Math.Sin(elapsed / 9.0 + channelIndex * 0.35);
            var noiseP = (_random.NextDouble() * 2 - 1) * _settings.SimulatedComNoiseDbm;
            var power = centerPower + swing * 0.5 * shape + noiseP;

            if (power < _settings.SimulatedPowerMin)
                power = _settings.SimulatedPowerMin;
            if (power > _settings.SimulatedPowerMax)
                power = _settings.SimulatedPowerMax;

            // Typical serial text payload.
            return $"OK CH={channelIndex + 1} WL={wl:F4} PWR={power:F2}";
        }
    }

    private static bool TryParseResponse(string response, out double wavelength, out double power)
    {
        wavelength = 0;
        power = 0;

        var parts = response.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part.StartsWith("WL=", StringComparison.OrdinalIgnoreCase) &&
                double.TryParse(part[3..], out var wl))
            {
                wavelength = wl;
            }
            else if (part.StartsWith("PWR=", StringComparison.OrdinalIgnoreCase) &&
                     double.TryParse(part[4..], out var p))
            {
                power = p;
            }
        }

        return wavelength > 0;
    }
}
