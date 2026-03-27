using LightSourceMonitor.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace LightSourceMonitor.Services.Trend;

public interface ITrendService
{
    Task<IList<MeasurementRecord>> GetTrendDataAsync(int channelId, DateTime from, DateTime to, int maxPoints = 2000);
    Task<IList<MeasurementRecord>> GetTrendDataAsync(int[] channelIds, DateTime from, DateTime to, int maxPoints = 2000);
    Task CleanupOldDataAsync(int retentionDays = 30);
}

public class TrendService : ITrendService
{
    private readonly IServiceProvider _serviceProvider;

    public TrendService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<IList<MeasurementRecord>> GetTrendDataAsync(int channelId, DateTime from, DateTime to, int maxPoints = 2000)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.MonitorDbContext>();

        var query = db.MeasurementRecords
            .Where(r => r.ChannelId == channelId && r.Timestamp >= from && r.Timestamp <= to)
            .OrderBy(r => r.Timestamp);

        var totalCount = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(query);

        if (totalCount <= maxPoints)
        {
            return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(query);
        }

        var allData = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(query);
        return LttbDownsample(allData, maxPoints);
    }

    public async Task<IList<MeasurementRecord>> GetTrendDataAsync(int[] channelIds, DateTime from, DateTime to, int maxPoints = 2000)
    {
        var result = new List<MeasurementRecord>();
        foreach (var channelId in channelIds)
        {
            var channelData = await GetTrendDataAsync(channelId, from, to, maxPoints);
            result.AddRange(channelData);
        }
        result.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return result;
    }

    /// <summary>
    /// Largest-Triangle-Three-Buckets downsampling preserves visual shape
    /// while reducing point count to the target threshold.
    /// </summary>
    private static List<MeasurementRecord> LttbDownsample(List<MeasurementRecord> data, int threshold)
    {
        if (data.Count <= threshold) return data;

        var sampled = new List<MeasurementRecord>(threshold);
        sampled.Add(data[0]);

        double bucketSize = (double)(data.Count - 2) / (threshold - 2);

        int aIndex = 0;
        for (int i = 0; i < threshold - 2; i++)
        {
            int bucketStart = (int)Math.Floor((i + 1) * bucketSize) + 1;
            int bucketEnd = (int)Math.Floor((i + 2) * bucketSize) + 1;
            if (bucketEnd > data.Count - 1) bucketEnd = data.Count - 1;

            int nextBucketStart = bucketEnd;
            int nextBucketEnd = (int)Math.Floor((i + 3) * bucketSize) + 1;
            if (nextBucketEnd > data.Count - 1) nextBucketEnd = data.Count - 1;
            if (i == threshold - 3) nextBucketEnd = data.Count - 1;

            double avgX = 0, avgY = 0;
            int nextCount = nextBucketEnd - nextBucketStart + 1;
            for (int j = nextBucketStart; j <= nextBucketEnd; j++)
            {
                avgX += data[j].Timestamp.Ticks;
                avgY += data[j].Power;
            }
            avgX /= nextCount;
            avgY /= nextCount;

            double maxArea = -1;
            int maxIndex = bucketStart;
            double pointAx = data[aIndex].Timestamp.Ticks;
            double pointAy = data[aIndex].Power;

            for (int j = bucketStart; j < bucketEnd; j++)
            {
                double area = Math.Abs(
                    (pointAx - avgX) * (data[j].Power - pointAy) -
                    (pointAx - data[j].Timestamp.Ticks) * (avgY - pointAy));
                if (area > maxArea)
                {
                    maxArea = area;
                    maxIndex = j;
                }
            }

            sampled.Add(data[maxIndex]);
            aIndex = maxIndex;
        }

        sampled.Add(data[^1]);
        return sampled;
    }

    public async Task CleanupOldDataAsync(int retentionDays = 30)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.MonitorDbContext>();
        var cutoff = DateTime.Now.AddDays(-retentionDays);

        var batchSize = 5000;
        int totalDeleted = 0;

        while (true)
        {
            var batch = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .ToListAsync(db.MeasurementRecords
                    .Where(r => r.Timestamp < cutoff)
                    .Take(batchSize));

            if (batch.Count == 0) break;

            db.MeasurementRecords.RemoveRange(batch);
            await db.SaveChangesAsync();
            totalDeleted += batch.Count;
        }

        if (totalDeleted > 0)
            Log.Information("Data retention: deleted {Count} old MeasurementRecords", totalDeleted);
    }
}
