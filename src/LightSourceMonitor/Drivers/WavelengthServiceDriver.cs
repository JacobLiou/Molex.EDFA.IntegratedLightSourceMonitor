using System.Net.Sockets;
using System.Text;
using LightSourceMonitor.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightSourceMonitor.Drivers;

public class WavelengthServiceDriver : IWavelengthServiceDriver
{
    private readonly ILogger<WavelengthServiceDriver> _logger;
    private readonly WavelengthServiceSettings _settings;
    private TcpClient? _tcpClient;
    private NetworkStream? _networkStream;
    private bool _disposed;
    private readonly Random _random = new();
    private readonly object _lockObject = new();

    public bool IsConnected { get; private set; }

    public WavelengthServiceDriver(ILogger<WavelengthServiceDriver> logger, IOptions<WavelengthServiceSettings> options)
    {
        _logger = logger;
        _settings = options.Value;
    }

    public async Task<bool> ConnectAsync(string host, int port)
    {
        if (_settings.IsSimulated)
        {
            _logger.LogInformation("WavelengthServiceDriver in SIMULATED mode");
            IsConnected = true;
            return true;
        }

        try
        {
            lock (_lockObject)
            {
                _tcpClient = new TcpClient();
            }

            using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(_settings.GetEffectiveTimeoutMs())))
            {
                try
                {
                    await _tcpClient.ConnectAsync(host, port, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    _tcpClient?.Dispose();
                    _tcpClient = null;
                    _logger.LogError("Connection timeout to {Host}:{Port}", host, port);
                    return false;
                }
            }

            lock (_lockObject)
            {
                _networkStream = _tcpClient?.GetStream();
            }

            IsConnected = true;
            _logger.LogInformation("Connected to WavelengthService at {Host}:{Port}", host, port);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to WavelengthService");
            IsConnected = false;
            return false;
        }
    }

    public Task<bool> DisconnectAsync()
    {
        try
        {
            lock (_lockObject)
            {
                _networkStream?.Dispose();
                _networkStream = null;
                _tcpClient?.Dispose();
                _tcpClient = null;
            }

            IsConnected = false;
            _logger.LogInformation("Disconnected from WavelengthService");
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during disconnect");
            return Task.FromResult(false);
        }
    }

    public async Task<(double wavelength, double power)> GetWavelengthAsync(string deviceId, int channelIndex)
    {
        try
        {
            if (_settings.IsSimulated)
            {
                return GenerateSimulatedData();
            }

            if (!IsConnected || _networkStream == null)
            {
                _logger.LogWarning("Service not connected, returning simulated data");
                return GenerateSimulatedData();
            }

            // Build request: format: "QUERY {deviceId} {channelIndex}\n"
            var request = $"QUERY {deviceId} {channelIndex}\n";
            var requestBytes = Encoding.UTF8.GetBytes(request);

            lock (_lockObject)
            {
                _networkStream?.Write(requestBytes, 0, requestBytes.Length);
                _networkStream?.Flush();
            }

            // Read response with timeout
            var buffer = new byte[256];
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(_settings.GetEffectiveTimeoutMs())))
                {
                    int bytesRead = await _networkStream!.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                    if (bytesRead <= 0)
                    {
                        _logger.LogWarning("No data received from WavelengthService");
                        return GenerateSimulatedData();
                    }

                    var response = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

                    // Parse response: format "wavelength=XXXX power=XXXX"
                    if (ParseResponse(response, out var wavelength, out var power))
                    {
                        return (wavelength, power);
                    }

                    _logger.LogWarning("Failed to parse response: {Response}", response);
                    return GenerateSimulatedData();
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Read timeout from WavelengthService for device {Device} channel {Channel}", deviceId, channelIndex);
                return GenerateSimulatedData();
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("GetWavelengthAsync cancelled");
            return GenerateSimulatedData();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying wavelength from service");
            return GenerateSimulatedData();
        }
    }

    private (double wavelength, double power) GenerateSimulatedData()
    {
        lock (_lockObject)
        {
            var wavelength = _settings.SimulatedWavelengthMin +
                (_settings.SimulatedWavelengthMax - _settings.SimulatedWavelengthMin) * _random.NextDouble();
            var power = _settings.SimulatedPowerMin +
                (_settings.SimulatedPowerMax - _settings.SimulatedPowerMin) * _random.NextDouble();

            return (wavelength, power);
        }
    }

    private bool ParseResponse(string response, out double wavelength, out double power)
    {
        wavelength = 0;
        power = 0;

        try
        {
            var parts = response.Split(' ');
            foreach (var part in parts)
            {
                if (part.StartsWith("wavelength=") && double.TryParse(part.Substring(11), out var wl))
                {
                    wavelength = wl;
                }
                else if (part.StartsWith("power=") && double.TryParse(part.Substring(6), out var pw))
                {
                    power = pw;
                }
            }

            return wavelength > 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error parsing response");
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            DisconnectAsync().Wait(TimeSpan.FromSeconds(2));
        }
        catch { }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
