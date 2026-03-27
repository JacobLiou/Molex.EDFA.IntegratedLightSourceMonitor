using LightSourceMonitor.Data;
using LightSourceMonitor.Drivers;
using LightSourceMonitor.Helpers;
using LightSourceMonitor.Models;
using LightSourceMonitor.Services.Alarm;
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
    private readonly IPdArrayDriver _pdDriver;
    private readonly IWavelengthMeterDriver _wmDriver;
    private readonly DriverSettings _driverSettings;
    private CancellationTokenSource? _cts;
    private Task? _runningTask;
    private int _sampleCount;
    private int _consecutiveErrors;

    public bool IsRunning { get; private set; }
    public event Action<Dictionary<int, MeasurementRecord>>? DataAcquired;

    public int SamplingIntervalMs { get; set; } = 2000;
    public int WmSweepEveryN { get; set; } = 5;
    public int DbWriteEveryN { get; set; } = 1;

    public AcquisitionService(
        IServiceProvider services,
        ILogger<AcquisitionService> logger,
        IAlarmService alarmService,
        IPdArrayDriver pdDriver,
        IWavelengthMeterDriver wmDriver,
        IOptions<DriverSettings> driverOptions)
    {
        _services = services;
        _logger = logger;
        _alarmService = alarmService;
        _pdDriver = pdDriver;
        _wmDriver = wmDriver;
        _driverSettings = driverOptions.Value;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning) return Task.CompletedTask;

        try
        {
            if (!_pdDriver.IsOpen)
            {
                var pdOpenArg = string.IsNullOrWhiteSpace(_driverSettings.PdOpenArgument)
                    ? "SIM"
                    : _driverSettings.PdOpenArgument;
                _pdDriver.Open(pdOpenArg);
                _pdDriver.Initialize();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize PD driver");
            throw;
        }

        try
        {
            if (!_wmDriver.IsInitialized)
            {
                _wmDriver.Init(_driverSettings.WmConfigXmlPath ?? string.Empty);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize WM driver");
            throw;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        IsRunning = true;
        _runningTask = Task.Run(() => AcquisitionLoop(_cts.Token), _cts.Token);
        _logger.LogInformation("Acquisition started (PD={PdSN}, WM={WmInit})", _pdDriver.DeviceSN, _wmDriver.IsInitialized);
        return Task.CompletedTask;
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

        try { _pdDriver.Close(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Error closing PD driver"); }

        try { _wmDriver.Close(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Error closing WM driver"); }

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

                using var scope = _services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();
                var channels = await db.LaserChannels
                    .Where(c => c.IsEnabled)
                    .OrderBy(c => c.ChannelIndex)
                    .ToListAsync(ct);

                if (channels.Count == 0)
                {
                    await Task.Delay(SamplingIntervalMs, ct);
                    continue;
                }

                var powers = _pdDriver.GetActualPower(channels.Count);
                bool doWmSweep = (_sampleCount % WmSweepEveryN == 0);

                var batch = new Dictionary<int, MeasurementRecord>();

                for (int i = 0; i < channels.Count; i++)
                {
                    var ch = channels[i];
                    double power = powers != null && i < powers.Length ? powers[i] : 0;
                    double wavelength = ch.SpecWavelength;

                    if (doWmSweep)
                    {
                        try
                        {
                            _wmDriver.SetParameters(i, 0, ch.SpecWavelength - 5, ch.SpecWavelength + 5, 3.0, -30.0);
                            _wmDriver.ExecuteSingleSweep(i);
                            var result = _wmDriver.GetResult(i);
                            if (result.HasValue && result.Value.count > 0)
                                wavelength = result.Value.wavelengths[0];
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "WM sweep failed for channel {Ch}", ch.ChannelName);
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
}
