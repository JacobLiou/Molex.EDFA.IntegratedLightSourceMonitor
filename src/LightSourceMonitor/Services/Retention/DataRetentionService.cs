using LightSourceMonitor.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LightSourceMonitor.Services.Retention;

public class DataRetentionService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<DataRetentionService> _logger;

    private const int MeasurementRetentionDays = 30;
    private const int AlarmRetentionDays = 90;
    private static readonly TimeSpan RunInterval = TimeSpan.FromHours(1);
    private const int BatchSize = 5000;

    public DataRetentionService(IServiceProvider services, ILogger<DataRetentionService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DataRetentionService started (measurements={MeasDays}d, alarms={AlarmDays}d, interval={Interval}h)",
            MeasurementRetentionDays, AlarmRetentionDays, RunInterval.TotalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(RunInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await RunCleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Data retention cleanup failed");
            }
        }

        _logger.LogInformation("DataRetentionService stopped");
    }

    private async Task RunCleanupAsync(CancellationToken ct)
    {
        var measurementCutoff = DateTime.Now.AddDays(-MeasurementRetentionDays);
        var alarmCutoff = DateTime.Now.AddDays(-AlarmRetentionDays);
        int totalMeasDeleted = 0;
        int totalAlarmDeleted = 0;

        while (!ct.IsCancellationRequested)
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();

            var batch = await db.MeasurementRecords
                .Where(r => r.Timestamp < measurementCutoff)
                .OrderBy(r => r.Id)
                .Take(BatchSize)
                .ToListAsync(ct);

            if (batch.Count == 0) break;

            db.MeasurementRecords.RemoveRange(batch);
            await db.SaveChangesAsync(ct);
            totalMeasDeleted += batch.Count;
        }

        int totalWmDeleted = 0;
        while (!ct.IsCancellationRequested)
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();

            var wmBatch = await db.WavelengthMeterSnapshots
                .Where(r => r.Timestamp < measurementCutoff)
                .OrderBy(r => r.Id)
                .Take(BatchSize)
                .ToListAsync(ct);

            if (wmBatch.Count == 0) break;

            db.WavelengthMeterSnapshots.RemoveRange(wmBatch);
            await db.SaveChangesAsync(ct);
            totalWmDeleted += wmBatch.Count;
        }

        while (!ct.IsCancellationRequested)
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();

            var batch = await db.AlarmEvents
                .Where(a => a.OccurredAt < alarmCutoff)
                .OrderBy(a => a.Id)
                .Take(BatchSize)
                .ToListAsync(ct);

            if (batch.Count == 0) break;

            db.AlarmEvents.RemoveRange(batch);
            await db.SaveChangesAsync(ct);
            totalAlarmDeleted += batch.Count;
        }

        if (totalMeasDeleted > 0 || totalWmDeleted > 0 || totalAlarmDeleted > 0)
        {
            _logger.LogInformation(
                "Data retention cleanup: deleted {MeasCount} measurements, {WmCount} WM snapshots (>{MeasDays}d), {AlarmCount} alarms (>{AlarmDays}d)",
                totalMeasDeleted, totalWmDeleted, MeasurementRetentionDays, totalAlarmDeleted, AlarmRetentionDays);

            try
            {
                using var scope = _services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();
                await db.Database.ExecuteSqlRawAsync("PRAGMA incremental_vacuum;", ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PRAGMA incremental_vacuum failed");
            }
        }
    }
}
