using LightSourceMonitor.Data;
using LightSourceMonitor.Drivers;
using LightSourceMonitor.Helpers;
using LightSourceMonitor.Models;
using LightSourceMonitor.Services.Alarm;
using LightSourceMonitor.Services.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightSourceMonitor.Services.Acquisition;

public class AcquisitionService : IAcquisitionService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<AcquisitionService> _logger;
    private readonly IAlarmService _alarmService;
    private readonly IChannelCatalog _channelCatalog;
    private readonly IPdDriverManager _pdDriverManager;
    private readonly IWavelengthMeterDriver _wmDriver;
    private readonly IWavelengthServiceDriver _wmServiceDriver;
    private readonly DriverSettings _driverSettings;
    private readonly WavelengthServiceSettings _wmServiceSettings;
    private CancellationTokenSource? _cts;
    private Task? _runningTask;
    private int _sampleCount;
    private int _consecutiveErrors;
    private bool _pdConnected;
    private readonly Dictionary<string, int> _deviceReconnectCounters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _deviceStates = new(StringComparer.OrdinalIgnoreCase);

    public bool IsRunning { get; private set; }
    public bool IsPdConnected => _pdConnected;
    public IReadOnlyDictionary<string, bool> PdDeviceStates => _deviceStates;
    public event Action<Dictionary<int, MeasurementRecord>>? DataAcquired;
    public event Action<IReadOnlyDictionary<string, WbaTelemetrySnapshot>>? WbaTelemetryAcquired;
    public event Action<bool>? PdConnectionChanged;
    public event Action<IReadOnlyDictionary<string, bool>>? PdDeviceConnectionChanged;

    public int SamplingIntervalMs { get; set; } = 5000;
    public int WmSweepEveryN { get; set; } = 10;
    public int DbWriteEveryN { get; set; } = 10;

    public AcquisitionService(
        IServiceProvider services,
        ILogger<AcquisitionService> logger,
        IAlarmService alarmService,
        IChannelCatalog channelCatalog,
        IPdDriverManager pdDriverManager,
        IWavelengthMeterDriver wmDriver,
        IWavelengthServiceDriver wmServiceDriver,
        IOptions<DriverSettings> driverOptions,
        IOptions<WavelengthServiceSettings> wmServiceOptions)
    {
        _services = services;
        _logger = logger;
        _alarmService = alarmService;
        _channelCatalog = channelCatalog;
        _pdDriverManager = pdDriverManager;
        _wmDriver = wmDriver;
        _wmServiceDriver = wmServiceDriver;
        _driverSettings = driverOptions.Value;
        _wmServiceSettings = wmServiceOptions.Value;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning) return;

        var effectiveDevices = _driverSettings.GetEffectiveDevices();
        ValidateDeviceSettings(effectiveDevices);
        _pdDriverManager.ConfigureDevices(effectiveDevices);

        _deviceReconnectCounters.Clear();
        _deviceStates.Clear();
        foreach (var device in effectiveDevices)
        {
            _deviceReconnectCounters[device.DeviceSN] = 0;
            _deviceStates[device.DeviceSN] = _pdDriverManager.TryReconnect(device.DeviceSN);
        }

        UpdateAndPublishPdStates(forceNotify: true);

        try
        {
            if (!_wmDriver.IsInitialized)
                _wmDriver.Init(_driverSettings.WmConfigXmlPath ?? string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WM driver initialization failed");
        }

        try
        {
            await _wmServiceDriver.ConnectAsync(_wmServiceSettings.Host, _wmServiceSettings.Port);
            _logger.LogInformation("WavelengthService connection initiated");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WavelengthService connection failed (will use mock mode)");
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        IsRunning = true;
        _runningTask = Task.Run(() => AcquisitionLoop(_cts.Token), _cts.Token);
        _logger.LogInformation("Acquisition started (PD devices={Count}, WM={WmInit}, Service={Service})", _deviceStates.Count, _wmDriver.IsInitialized, _wmServiceDriver.IsConnected);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning || _cts == null) return;

        _logger.LogInformation("Acquisition stopping...");
        _cts.Cancel();
        IsRunning = false;

        if (_runningTask != null)
        {
            try { await _runningTask.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (OperationCanceledException) { }
            catch (TimeoutException)
            {
                _logger.LogWarning("Acquisition loop did not stop within timeout");
            }
        }

        try { _pdDriverManager.CloseAll(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Error closing PD drivers"); }

        try { _wmDriver.Close(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Error closing WM driver"); }

        try { await _wmServiceDriver.DisconnectAsync(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Error disconnecting WM service"); }

        _logger.LogInformation("Acquisition stopped");
    }

    private async Task AcquisitionLoop(CancellationToken ct)
    {
        _logger.LogInformation("Acquisition loop entered");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.Now;

                var channels = _channelCatalog.GetEnabledChannels();

                if (channels.Count == 0)
                {
                    await Task.Delay(SamplingIntervalMs, ct);
                    continue;
                }

                using var scope = _services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();

                bool doWmSweep = (_sampleCount % WmSweepEveryN == 0);
                var batch = new Dictionary<int, MeasurementRecord>();
                var wbaBatch = new Dictionary<string, WbaTelemetrySnapshot>(StringComparer.OrdinalIgnoreCase);

                var grouped = channels
                    .GroupBy(c => c.DeviceSN)
                    .ToList();

                foreach (var deviceGroup in grouped)
                {
                    var deviceSn = deviceGroup.Key;
                    var orderedChannels = deviceGroup
                        .OrderBy(c => c.ChannelIndex)
                        .ToList();

                    if (!_pdDriverManager.TryReadPower(deviceSn, orderedChannels.Count, out var powers))
                    {
                        _deviceReconnectCounters[deviceSn] = _deviceReconnectCounters.GetValueOrDefault(deviceSn) + 1;
                        if (_deviceReconnectCounters[deviceSn] % 30 == 0)
                        {
                            _pdDriverManager.TryReconnect(deviceSn);
                        }

                        _deviceStates[deviceSn] = false;
                        continue;
                    }

                    _deviceReconnectCounters[deviceSn] = 0;
                    _deviceStates[deviceSn] = true;

                    if (_pdDriverManager.TryReadWbaTelemetry(deviceSn, out var wbaTelemetry) && wbaTelemetry != null)
                    {
                        wbaBatch[deviceSn] = wbaTelemetry;
                    }

                    for (int i = 0; i < orderedChannels.Count; i++)
                    {
                        var ch = orderedChannels[i];
                        double power = powers != null && i < powers.Length ? powers[i] : 0;
                        double wavelength = ch.SpecWavelength;

                        if (doWmSweep)
                        {
                            try
                            {
                                var (wmWavelength, wmPower) = await _wmServiceDriver.GetWavelengthAsync(deviceSn, i);
                                wavelength = wmWavelength;
                                // Note: wmPower is from service, but we use PD power for consistency
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "WM service query failed for channel {Ch}, using spec value", ch.ChannelName);
                            }
                        }

                        var record = new MeasurementRecord
                        {
                            ChannelId = ch.Id,
                            Timestamp = now,
                            Power = power,
                            Wavelength = wavelength
                        };
                        batch[ch.Id] = record;
                    }
                }

                UpdateAndPublishPdStates();

                if (batch.Count == 0)
                {
                    await Task.Delay(SamplingIntervalMs, ct);
                    continue;
                }

                _sampleCount++;

                if (_sampleCount % DbWriteEveryN == 0)
                {
                    try
                    {
                        db.MeasurementRecords.AddRange(batch.Values);
                        await db.SaveChangesAsync(ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to write measurement batch to DB");
                    }
                }

                foreach (var kvp in batch)
                {
                    try
                    {
                        var ch = channels.First(c => c.Id == kvp.Key);
                        await _alarmService.EvaluateAsync(kvp.Value, ch);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Alarm evaluation failed for channelId={Id}", kvp.Key);
                    }
                }

                DataAcquired.SafeInvoke(batch, nameof(DataAcquired));
                if (wbaBatch.Count > 0)
                    WbaTelemetryAcquired.SafeInvoke(wbaBatch, nameof(WbaTelemetryAcquired));

                _consecutiveErrors = 0;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _consecutiveErrors++;
                _logger.LogError(ex, "Acquisition cycle error (consecutive: {Count})", _consecutiveErrors);

                if (_consecutiveErrors > 50)
                {
                    _logger.LogCritical("Too many consecutive errors ({Count}), pausing acquisition for 30s", _consecutiveErrors);
                    try { await Task.Delay(30_000, ct); } catch { break; }
                    _consecutiveErrors = 0;
                }
            }

            try { await Task.Delay(SamplingIntervalMs, ct); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("Acquisition loop exited");
    }

    private void ValidateDeviceSettings(IReadOnlyList<PdDeviceSettings> devices)
    {
        var duplicateSn = devices
            .GroupBy(d => d.DeviceSN, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicateSn.Count > 0)
            throw new InvalidOperationException($"Duplicate DeviceSN in Driver.Devices: {string.Join(", ", duplicateSn)}");

        var duplicateUsb = devices
            .GroupBy(d => d.UsbAddress, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicateUsb.Count > 0)
            throw new InvalidOperationException($"Duplicate UsbAddress in Driver.Devices: {string.Join(", ", duplicateUsb)}");

        foreach (var device in devices)
        {
            if (string.IsNullOrWhiteSpace(device.DeviceSN))
                throw new InvalidOperationException("DeviceSN must not be empty in Driver.Devices");
            if (string.IsNullOrWhiteSpace(device.UsbAddress))
                throw new InvalidOperationException($"UsbAddress must not be empty for device {device.DeviceSN}");
        }
    }

    private void UpdateAndPublishPdStates(bool forceNotify = false)
    {
        var anyConnectedNow = _deviceStates.Values.Any(v => v);
        if (forceNotify || anyConnectedNow != _pdConnected)
        {
            _pdConnected = anyConnectedNow;
            PdConnectionChanged?.Invoke(anyConnectedNow);
        }

        PdDeviceConnectionChanged?.Invoke(new Dictionary<string, bool>(_deviceStates, StringComparer.OrdinalIgnoreCase));
    }
}
