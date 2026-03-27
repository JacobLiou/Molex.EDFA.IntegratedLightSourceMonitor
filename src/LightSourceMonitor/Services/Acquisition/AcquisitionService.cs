using System.Threading.Channels;
using LightSourceMonitor.Data;
using LightSourceMonitor.Drivers;
using LightSourceMonitor.Models;
using LightSourceMonitor.Services.Alarm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LightSourceMonitor.Services.Acquisition;

public class AcquisitionService : IAcquisitionService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<AcquisitionService> _logger;
    private readonly Channel<MeasurementRecord> _channel;
    private readonly IAlarmService _alarmService;
    private CancellationTokenSource? _cts;
    private Task? _runningTask;
    private int _sampleCount;

    public bool IsRunning { get; private set; }
    public event Action<Dictionary<int, MeasurementRecord>>? DataAcquired;

    public int SamplingIntervalMs { get; set; } = 2000;
    public int WmSweepEveryN { get; set; } = 100;
    public int DbWriteEveryN { get; set; } = 10;

    public AcquisitionService(
        IServiceProvider services,
        ILogger<AcquisitionService> logger,
        Channel<MeasurementRecord> channel,
        IAlarmService alarmService)
    {
        _services = services;
        _logger = logger;
        _channel = channel;
        _alarmService = alarmService;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning) return Task.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        IsRunning = true;
        _runningTask = Task.Run(() => AcquisitionLoop(_cts.Token), _cts.Token);
        _logger.LogInformation("Acquisition started");
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning || _cts == null) return;

        _cts.Cancel();
        IsRunning = false;

        if (_runningTask != null)
        {
            try { await _runningTask; }
            catch (OperationCanceledException) { }
        }

        _logger.LogInformation("Acquisition stopped");
    }

    private async Task AcquisitionLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var batch = new Dictionary<int, MeasurementRecord>();
                var now = DateTime.Now;

                using var scope = _services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();
                var channels = await db.LaserChannels
                    .Where(c => c.IsEnabled)
                    .ToListAsync(ct);

                if (channels.Count == 0)
                {
                    await Task.Delay(SamplingIntervalMs, ct);
                    continue;
                }

                foreach (var channel in channels)
                {
                    var record = new MeasurementRecord
                    {
                        ChannelId = channel.Id,
                        Timestamp = now,
                        Power = 0,
                        Wavelength = 0
                    };
                    batch[channel.Id] = record;
                }

                _sampleCount++;

                if (_sampleCount % DbWriteEveryN == 0)
                {
                    db.MeasurementRecords.AddRange(batch.Values);
                    await db.SaveChangesAsync(ct);
                }

                foreach (var kvp in batch)
                {
                    await _channel.Writer.WriteAsync(kvp.Value, ct);
                    var ch = channels.First(c => c.Id == kvp.Key);
                    await _alarmService.EvaluateAsync(kvp.Value, ch);
                }

                DataAcquired?.Invoke(batch);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Acquisition cycle error");
            }

            try { await Task.Delay(SamplingIntervalMs, ct); }
            catch (OperationCanceledException) { break; }
        }
    }
}
