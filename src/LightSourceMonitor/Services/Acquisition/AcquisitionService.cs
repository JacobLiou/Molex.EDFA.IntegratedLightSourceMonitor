using LightSourceMonitor.Data;
using LightSourceMonitor.Drivers;
using LightSourceMonitor.Helpers;
using LightSourceMonitor.Models;
using LightSourceMonitor.Services.Alarm;
using LightSourceMonitor.Services.Channels;
using LightSourceMonitor.Services.Config;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightSourceMonitor.Services.Acquisition;

public class AcquisitionService : IAcquisitionService
{
    private const int MinSamplingIntervalMs = 100;
    private const int MinSweepEveryN = 1;
    private const int MinDbWriteEveryN = 1;

    private readonly IServiceProvider _services;
    private readonly ILogger<AcquisitionService> _logger;
    private readonly IAlarmService _alarmService;
    private readonly IChannelCatalog _channelCatalog;
    private readonly IPdDriverManager _pdDriverManager;
    private readonly IWbaDeviceManager _wbaDeviceManager;
    private readonly IWavelengthMeterDriver _wmDriver;
    private readonly IWavelengthServiceDriver _wmServiceDriver;
    private readonly DriverSettings _driverSettings;
    private readonly WavelengthServiceSettings _wmServiceSettings;
    private readonly IRuntimeJsonConfigService _runtimeJsonConfig;
    private CancellationTokenSource? _cts;
    private Task? _runningTask;
    private int _sampleCount;
    private int _consecutiveErrors;
    private bool _pdConnected;
    private readonly Dictionary<string, int> _deviceReconnectCounters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _deviceStates = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _wbaDeviceSns = new();

    public bool IsRunning { get; private set; }
    public bool IsPdConnected => _pdConnected;
    public IReadOnlyDictionary<string, bool> PdDeviceStates => _deviceStates;
    public event Action<Dictionary<int, MeasurementRecord>>? DataAcquired;
    public event Action<WavelengthTableSnapshot>? WavelengthTableUpdated;
    public event Action<IReadOnlyDictionary<string, WbaTelemetrySnapshot>>? WbaTelemetryAcquired;
    public event Action<DateTime>? AcquisitionCycleCompleted;
    public event Action<bool>? PdConnectionChanged;
    public event Action<IReadOnlyDictionary<string, bool>>? PdDeviceConnectionChanged;

    public int SamplingIntervalMs { get; set; } = 5000;
    public int WmSweepEveryN { get; set; } = 20;
    public int WbaSweepEveryN { get; set; } = 1;
    public int DbWriteEveryN { get; set; } = 20;

    public AcquisitionService(
        IServiceProvider services,
        ILogger<AcquisitionService> logger,
        IAlarmService alarmService,
        IChannelCatalog channelCatalog,
        IPdDriverManager pdDriverManager,
        IWbaDeviceManager wbaDeviceManager,
        IWavelengthMeterDriver wmDriver,
        IWavelengthServiceDriver wmServiceDriver,
        IOptions<DriverSettings> driverOptions,
        IOptions<WavelengthServiceSettings> wmServiceOptions,
        IRuntimeJsonConfigService runtimeJsonConfig)
    {
        _services = services;
        _logger = logger;
        _alarmService = alarmService;
        _channelCatalog = channelCatalog;
        _pdDriverManager = pdDriverManager;
        _wbaDeviceManager = wbaDeviceManager;
        _wmDriver = wmDriver;
        _wmServiceDriver = wmServiceDriver;
        _driverSettings = driverOptions.Value;
        _wmServiceSettings = wmServiceOptions.Value;
        _runtimeJsonConfig = runtimeJsonConfig;

        LoadAcquisitionConfigAsync().SafeFireAndForget("AcquisitionService.LoadAcquisitionConfig");
    }

    private async Task LoadAcquisitionConfigAsync()
    {
        try
        {
            var acqConfig = await _runtimeJsonConfig.LoadAcquisitionAsync();
            SamplingIntervalMs = acqConfig.SamplingIntervalMs;
            WmSweepEveryN = acqConfig.WmSweepEveryN;
            WbaSweepEveryN = acqConfig.WbaSweepEveryN > 0 ? acqConfig.WbaSweepEveryN : 1;
            DbWriteEveryN = acqConfig.DbWriteEveryN;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load acquisition config from JSON");
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning) return;

        var effectiveDevices = _driverSettings.GetEffectiveDevices();
        ValidateDeviceSettings(effectiveDevices);
        _pdDriverManager.ConfigureDevices(effectiveDevices);

        var effectiveWbaDevices = _driverSettings.GetEffectiveWbaDevices();
        _wbaDeviceManager.ConfigureDevices(effectiveWbaDevices);
        _wbaDeviceSns = effectiveWbaDevices.Select(d => d.DeviceSN).ToList();

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

        try { _wbaDeviceManager.CloseAll(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Error closing WBA devices"); }

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
            var samplingIntervalMs = SamplingIntervalMs <= 0 ? MinSamplingIntervalMs : SamplingIntervalMs;

            try
            {
                var wmSweepEveryN = WmSweepEveryN <= 0 ? MinSweepEveryN : WmSweepEveryN;
                var wbaSweepEveryN = WbaSweepEveryN <= 0 ? MinSweepEveryN : WbaSweepEveryN;
                var dbWriteEveryN = DbWriteEveryN <= 0 ? MinDbWriteEveryN : DbWriteEveryN;

                if (samplingIntervalMs != SamplingIntervalMs || wmSweepEveryN != WmSweepEveryN
                    || wbaSweepEveryN != WbaSweepEveryN || dbWriteEveryN != DbWriteEveryN)
                {
                    _logger.LogWarning(
                        "Invalid acquisition params detected (interval={Interval}, wmEvery={WmN}, wbaEvery={WbaN}, dbEvery={DbN}), fallback applied (interval={SafeInterval}, wmEvery={SafeWmN}, wbaEvery={SafeWbaN}, dbEvery={SafeDbN})",
                        SamplingIntervalMs,
                        WmSweepEveryN,
                        WbaSweepEveryN,
                        DbWriteEveryN,
                        samplingIntervalMs,
                        wmSweepEveryN,
                        wbaSweepEveryN,
                        dbWriteEveryN);
                }

                var now = DateTime.Now;

                var channels = _channelCatalog.GetEnabledChannels();

                if (channels.Count == 0)
                {
                    _sampleCount++;
                    // 即使没有通道，也要触发周期完成事件以更新UI
                    AcquisitionCycleCompleted?.Invoke(now);
                    await Task.Delay(samplingIntervalMs, ct);
                    continue;
                }

                using var scope = _services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();

                // Sweep WM table at configured cadence; allows first-cycle data when wmSweepEveryN=1.
                bool doWmSweep = _sampleCount % wmSweepEveryN == 0;
                bool doWbaSweep = _sampleCount % wbaSweepEveryN == 0;
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

                    var readChannelCount = orderedChannels.Max(c => c.ChannelIndex) + 1;
                    if (!_pdDriverManager.TryReadPower(deviceSn, readChannelCount, out var powers))
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

                    for (int i = 0; i < orderedChannels.Count; i++)
                    {
                        var ch = orderedChannels[i];
                        double power = powers != null && ch.ChannelIndex >= 0 && ch.ChannelIndex < powers.Length
                            ? powers[ch.ChannelIndex]
                            : 0;

                        var record = new MeasurementRecord
                        {
                            ChannelId = ch.Id,
                            Timestamp = now,
                            Power = power,
                            Wavelength = ch.SpecWavelength
                        };
                        batch[ch.Id] = record;
                    }
                }

                UpdateAndPublishPdStates();

                WavelengthTableSnapshot? wlTable = null;
                if (doWmSweep)
                {
                    try
                    {
                        wlTable = await BuildWavelengthTableSnapshotAsync(now, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Wavelength table sweep failed");
                    }
                }

                if (doWbaSweep)
                {
                    foreach (var wbaSn in _wbaDeviceSns)
                    {
                        if (_wbaDeviceManager.TryReadTelemetry(wbaSn, out var wbaTelemetry) && wbaTelemetry != null)
                            wbaBatch[wbaSn] = wbaTelemetry;
                    }
                }

                if (batch.Count == 0)
                {
                    if (wlTable != null)
                    {
                        WavelengthTableUpdated.SafeInvoke(wlTable, nameof(WavelengthTableUpdated));
                        await EvaluateWmWavelengthAlarmsAsync(wlTable, ct);
                        try
                        {
                            await PersistWmSnapshotAsync(db, wlTable, ct);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to persist WM trend snapshot");
                        }
                    }

                    if (wbaBatch.Count > 0)
                        WbaTelemetryAcquired.SafeInvoke(wbaBatch, nameof(WbaTelemetryAcquired));

                    _sampleCount++;

                    // 采集周期完成事件，即使没有PD数据也要触发
                    AcquisitionCycleCompleted?.Invoke(now);

                    await Task.Delay(samplingIntervalMs, ct);
                    continue;
                }

                // Push UI updates first; DB and alarm processing should not block dashboard refresh.
                DataAcquired.SafeInvoke(batch, nameof(DataAcquired));
                if (wlTable != null)
                {
                    WavelengthTableUpdated.SafeInvoke(wlTable, nameof(WavelengthTableUpdated));
                    await EvaluateWmWavelengthAlarmsAsync(wlTable, ct);
                    try
                    {
                        await PersistWmSnapshotAsync(db, wlTable, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to persist WM trend snapshot");
                    }
                }

                if (wbaBatch.Count > 0)
                    WbaTelemetryAcquired.SafeInvoke(wbaBatch, nameof(WbaTelemetryAcquired));

                _sampleCount++;

                if (_sampleCount == 1 || _sampleCount % dbWriteEveryN == 0)
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

                // 采集周期完成事件，使用统一的时间戳
                AcquisitionCycleCompleted?.Invoke(now);

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

            try { await Task.Delay(samplingIntervalMs, ct); }
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

    private static async Task PersistWmSnapshotAsync(MonitorDbContext db, WavelengthTableSnapshot snapshot,
        CancellationToken ct)
    {
        var list = new List<WmMeasurementRecord>(snapshot.Rows.Count);
        foreach (var row in snapshot.Rows)
        {
            list.Add(new WmMeasurementRecord
            {
                Timestamp = snapshot.Timestamp,
                QueryDeviceId = snapshot.QueryDeviceId ?? "",
                ChannelIndex = row.ChannelIndex,
                WavelengthNm = row.WavelengthNm,
                WmPowerDbm = row.IsValid ? row.WmPowerDbm : null,
                IsValid = row.IsValid
            });
        }

        db.WmMeasurementRecords.AddRange(list);
        await db.SaveChangesAsync(ct);
    }

    private async Task<WavelengthTableSnapshot> BuildWavelengthTableSnapshotAsync(DateTime now, CancellationToken ct)
    {
        var n = Math.Clamp(_wmServiceSettings.TableChannelCount, 1, 64);
        var effective = _driverSettings.GetEffectiveDevices();
        var deviceId = string.IsNullOrWhiteSpace(_wmServiceSettings.QueryDeviceId)
            ? effective.FirstOrDefault()?.DeviceSN ?? "WM"
            : _wmServiceSettings.QueryDeviceId.Trim();

        var rows = new List<WavelengthTableRow>(n);
        for (var j = 0; j < n; j++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var (wl, p) = await _wmServiceDriver.GetWavelengthAsync(deviceId, j);
                rows.Add(new WavelengthTableRow
                {
                    ChannelIndex = j,
                    WavelengthNm = wl,
                    WmPowerDbm = p,
                    IsValid = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WM table query failed for device {Device} index {Index}", deviceId, j);
                rows.Add(new WavelengthTableRow { ChannelIndex = j, IsValid = false });
            }
        }

        return new WavelengthTableSnapshot
        {
            QueryDeviceId = deviceId,
            Timestamp = now,
            Rows = rows
        };
    }

    private async Task EvaluateWmWavelengthAlarmsAsync(WavelengthTableSnapshot wlTable, CancellationToken ct)
    {
        var specs = _wmServiceSettings.ChannelSpecs;
        if (specs == null || specs.Count == 0)
            return;

        var deviceId = wlTable.QueryDeviceId;
        foreach (var row in wlTable.Rows)
        {
            ct.ThrowIfCancellationRequested();
            if (!row.IsValid)
                continue;

            var spec = _wmServiceSettings.ResolveChannelSpec(row.ChannelIndex);
            if (spec == null)
                continue;

            try
            {
                await _alarmService.EvaluateWavelengthServiceChannelAsync(
                    wlTable.Timestamp,
                    row.ChannelIndex,
                    row.WavelengthNm,
                    spec,
                    _wmServiceSettings,
                    deviceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WM wavelength alarm evaluation failed for index {Index}", row.ChannelIndex);
            }
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
