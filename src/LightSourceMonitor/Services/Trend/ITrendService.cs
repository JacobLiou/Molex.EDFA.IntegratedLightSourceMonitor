using LightSourceMonitor.Models;
using Microsoft.Extensions.DependencyInjection;

namespace LightSourceMonitor.Services.Trend;

public interface ITrendService
{
    Task<IList<MeasurementRecord>> GetTrendDataAsync(int channelId, DateTime from, DateTime to);
    Task<IList<MeasurementRecord>> GetTrendDataAsync(int[] channelIds, DateTime from, DateTime to);
    Task CleanupOldDataAsync(int retentionDays = 30);
}

public class TrendService : ITrendService
{
    private readonly IServiceProvider _serviceProvider;

    public TrendService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<IList<MeasurementRecord>> GetTrendDataAsync(int channelId, DateTime from, DateTime to)
    {
        return await GetTrendDataAsync(new[] { channelId }, from, to);
    }

    public async Task<IList<MeasurementRecord>> GetTrendDataAsync(int[] channelIds, DateTime from, DateTime to)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.MonitorDbContext>();
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            db.MeasurementRecords
                .Where(r => channelIds.Contains(r.ChannelId) && r.Timestamp >= from && r.Timestamp <= to)
                .OrderBy(r => r.Timestamp));
    }

    public async Task CleanupOldDataAsync(int retentionDays = 30)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.MonitorDbContext>();
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        var oldRecords = db.MeasurementRecords.Where(r => r.Timestamp < cutoff);
        db.MeasurementRecords.RemoveRange(oldRecords);
        await db.SaveChangesAsync();
    }
}
