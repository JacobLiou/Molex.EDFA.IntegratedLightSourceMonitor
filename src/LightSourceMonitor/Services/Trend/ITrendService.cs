using LightSourceMonitor.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace LightSourceMonitor.Services.Trend;

public interface ITrendService
{
    Task<IList<MeasurementRecord>> GetTrendDataAsync(int channelId, DateTime from, DateTime to, int maxPoints = 2000);

    Task<IList<MeasurementRecord>> GetTrendDataAsync(int[] channelIds, DateTime from, DateTime to, int maxPoints = 2000);

    /// <summary>
    /// 按 WM 路索引返回有效读数时间序列（每路单独降采样）。
    /// </summary>
    Task<IReadOnlyDictionary<int, IReadOnlyList<WmMeasurementRecord>>> GetWmTrendByChannelsAsync(
        string queryDeviceId,
        IReadOnlyList<int> channelIndices,
        DateTime from,
        DateTime to,
        int maxPointsPerSeries = 2000);

    Task CleanupOldDataAsync(int retentionDays = 30);
}

public class TrendService : ITrendService
{
    private readonly IServiceProvider _serviceProvider;

    public TrendService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<IList<MeasurementRecord>> GetTrendDataAsync(int channelId, DateTime from, DateTime to,
        int maxPoints = 2000)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.MonitorDbContext>();

        var query = db.MeasurementRecords
            .Where(r => r.ChannelId == channelId && r.Timestamp >= from && r.Timestamp <= to)
            .OrderBy(r => r.Timestamp);

        var totalCount = await query.CountAsync();
        if (totalCount <= maxPoints)
            return await query.ToListAsync();

        var allData = await query.ToListAsync();
        return LttbDownsample(allData, maxPoints, static r => r.Timestamp.Ticks, static r => r.Power);
    }

    public async Task<IList<MeasurementRecord>> GetTrendDataAsync(int[] channelIds, DateTime from, DateTime to,
        int maxPoints = 2000)
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

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<WmMeasurementRecord>>> GetWmTrendByChannelsAsync(
        string queryDeviceId,
        IReadOnlyList<int> channelIndices,
        DateTime from,
        DateTime to,
        int maxPointsPerSeries = 2000)
    {
        var key = queryDeviceId ?? "";
        var indexSet = channelIndices.Count > 0
            ? channelIndices.ToHashSet()
            : new HashSet<int>();

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.MonitorDbContext>();

        var query = db.WmMeasurementRecords
            .Where(r => r.QueryDeviceId == key && r.Timestamp >= from && r.Timestamp <= to && r.IsValid);

        if (indexSet.Count > 0)
            query = query.Where(r => indexSet.Contains(r.ChannelIndex));

        var all = await query
            .OrderBy(r => r.ChannelIndex)
            .ThenBy(r => r.Timestamp)
            .ToListAsync();

        var byChannel = all.GroupBy(r => r.ChannelIndex).ToDictionary(g => g.Key, g => g.ToList());

        var result = new Dictionary<int, IReadOnlyList<WmMeasurementRecord>>();
        foreach (var (idx, list) in byChannel)
        {
            if (list.Count == 0)
                continue;

            result[idx] = list.Count <= maxPointsPerSeries
                ? list
                : LttbDownsample(list, maxPointsPerSeries,
                    static r => r.Timestamp.Ticks,
                    static r => r.WavelengthNm);
        }

        return result;
    }

    /// <summary>
    /// Largest-Triangle-Three-Buckets downsampling preserves visual shape
    /// while reducing point count to the target threshold.
    /// </summary>
    private static List<T> LttbDownsample<T>(IReadOnlyList<T> data, int threshold, Func<T, long> xTicks,
        Func<T, double> yValue)
    {
        if (data.Count <= threshold)
            return data.ToList();

        var sampled = new List<T>(threshold);
        sampled.Add(data[0]);

        double bucketSize = (double)(data.Count - 2) / (threshold - 2);

        var aIndex = 0;
        for (var i = 0; i < threshold - 2; i++)
        {
            var bucketStart = (int)Math.Floor((i + 1) * bucketSize) + 1;
            var bucketEnd = (int)Math.Floor((i + 2) * bucketSize) + 1;
            if (bucketEnd > data.Count - 1)
                bucketEnd = data.Count - 1;

            var nextBucketStart = bucketEnd;
            var nextBucketEnd = (int)Math.Floor((i + 3) * bucketSize) + 1;
            if (nextBucketEnd > data.Count - 1)
                nextBucketEnd = data.Count - 1;
            if (i == threshold - 3)
                nextBucketEnd = data.Count - 1;

            double avgX = 0, avgY = 0;
            var nextCount = nextBucketEnd - nextBucketStart + 1;
            for (var j = nextBucketStart; j <= nextBucketEnd; j++)
            {
                avgX += xTicks(data[j]);
                avgY += yValue(data[j]);
            }

            avgX /= nextCount;
            avgY /= nextCount;

            double maxArea = -1;
            var maxIndex = bucketStart;
            var pointAx = xTicks(data[aIndex]);
            var pointAy = yValue(data[aIndex]);

            for (var j = bucketStart; j < bucketEnd; j++)
            {
                var area = Math.Abs(
                    (pointAx - avgX) * (yValue(data[j]) - pointAy) -
                    (pointAx - xTicks(data[j])) * (avgY - pointAy));
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

        const int batchSize = 5000;
        var totalPd = 0;
        while (true)
        {
            var batch = await db.MeasurementRecords
                .Where(r => r.Timestamp < cutoff)
                .Take(batchSize)
                .ToListAsync();

            if (batch.Count == 0)
                break;

            db.MeasurementRecords.RemoveRange(batch);
            await db.SaveChangesAsync();
            totalPd += batch.Count;
        }

        var totalWm = 0;
        while (true)
        {
            var batch = await db.WmMeasurementRecords
                .Where(r => r.Timestamp < cutoff)
                .Take(batchSize)
                .ToListAsync();

            if (batch.Count == 0)
                break;

            db.WmMeasurementRecords.RemoveRange(batch);
            await db.SaveChangesAsync();
            totalWm += batch.Count;
        }

        if (totalPd > 0)
            Log.Information("Data retention: deleted {Count} old MeasurementRecords", totalPd);
        if (totalWm > 0)
            Log.Information("Data retention: deleted {Count} old WmMeasurementRecords", totalWm);
    }
}
