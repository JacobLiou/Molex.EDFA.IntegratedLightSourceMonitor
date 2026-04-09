using System.Text.Json;
using LightSourceMonitor.Helpers;
using LightSourceMonitor.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Serilog;

namespace LightSourceMonitor.Services.Trend;

public interface ITrendService
{
    Task<IList<MeasurementRecord>> GetTrendDataAsync(int channelId, DateTime from, DateTime to, int maxPoints = 2000);
    Task<IList<MeasurementRecord>> GetTrendDataAsync(int[] channelIds, DateTime from, DateTime to, int maxPoints = 2000);

    /// <summary>波长计各路波长时间序列（已按路降采样）。</summary>
    Task<IReadOnlyList<WmTrendChannelSeries>> GetWmWavelengthTrendAsync(DateTime from, DateTime to, int maxPointsPerSeries = 2000);

    /// <summary>时间范围内的 WM 快照原始行（用于 CSV 导出等）。</summary>
    Task<IReadOnlyList<WavelengthMeterSnapshot>> GetWavelengthMeterSnapshotsAsync(DateTime from, DateTime to);

    Task<IReadOnlyList<WbaTrendSeries>> GetWbaTrendAsync(DateTime from, DateTime to, WbaTrendMetric metric,
        int maxPointsPerSeries = 2000);

    Task<IReadOnlyList<WbaTelemetryRecord>> GetWbaTelemetryRecordsAsync(DateTime from, DateTime to);

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

        var totalCount = await query.CountAsync();

        if (totalCount <= maxPoints)
            return await query.ToListAsync();

        var allData = await query.ToListAsync();
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

    public async Task<IReadOnlyList<WmTrendChannelSeries>> GetWmWavelengthTrendAsync(DateTime from, DateTime to,
        int maxPointsPerSeries = 2000)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.MonitorDbContext>();
        var wmSettings = scope.ServiceProvider.GetRequiredService<IOptions<WavelengthServiceSettings>>().Value;

        var rows = await db.WavelengthMeterSnapshots
            .Where(r => r.Timestamp >= from && r.Timestamp <= to)
            .OrderBy(r => r.Timestamp)
            .ToListAsync();

        if (rows.Count == 0)
            return Array.Empty<WmTrendChannelSeries>();

        var maxCh = 0;
        foreach (var r in rows)
        {
            var wl = WmSnapshotCsvCodec.ParseWavelengthSegments(r.OneTimeValues);
            maxCh = Math.Max(maxCh, wl.Count);
        }

        var perChannel = new List<(DateTime, double)>[maxCh];
        for (var i = 0; i < maxCh; i++)
            perChannel[i] = new List<(DateTime, double)>();

        foreach (var r in rows)
        {
            var wl = WmSnapshotCsvCodec.ParseWavelengthSegments(r.OneTimeValues);
            for (var i = 0; i < wl.Count && i < maxCh; i++)
            {
                if (wl[i] is { } v)
                    perChannel[i].Add((r.Timestamp, v));
            }
        }

        var result = new List<WmTrendChannelSeries>();
        for (var i = 0; i < maxCh; i++)
        {
            var pts = perChannel[i];
            if (pts.Count == 0)
                continue;

            var down = LttbDownsampleDy(pts, maxPointsPerSeries);
            var spec = wmSettings.ResolveChannelSpec(i);
            var name = spec is { ChannelName: var n } && !string.IsNullOrWhiteSpace(n)
                ? n.Trim()
                : $"路{i}";

            result.Add(new WmTrendChannelSeries
            {
                ChannelIndex = i,
                SeriesName = name,
                Points = down.Select(p => (p.t, p.y)).ToList()
            });
        }

        return result;
    }

    public async Task<IReadOnlyList<WavelengthMeterSnapshot>> GetWavelengthMeterSnapshotsAsync(DateTime from, DateTime to)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.MonitorDbContext>();
        return await db.WavelengthMeterSnapshots
            .Where(r => r.Timestamp >= from && r.Timestamp <= to)
            .OrderBy(r => r.Timestamp)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<WbaTrendSeries>> GetWbaTrendAsync(DateTime from, DateTime to,
        WbaTrendMetric metric, int maxPointsPerSeries = 2000)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.MonitorDbContext>();

        var rows = await db.WbaTelemetryRecords
            .Where(r => r.Timestamp >= from && r.Timestamp <= to)
            .OrderBy(r => r.Timestamp)
            .ToListAsync();

        if (rows.Count == 0)
            return Array.Empty<WbaTrendSeries>();

        var byDevice = rows.GroupBy(r => r.DeviceSN.Trim(), StringComparer.OrdinalIgnoreCase);
        var result = new List<WbaTrendSeries>();

        foreach (var g in byDevice)
        {
            var pts = new List<(DateTime t, double y)>();
            foreach (var r in g)
            {
                var v = ExtractWbaMetric(r, metric);
                if (v is { } val)
                    pts.Add((r.Timestamp, val));
            }

            if (pts.Count == 0)
                continue;

            var down = LttbDownsampleDy(pts, maxPointsPerSeries);
            result.Add(new WbaTrendSeries
            {
                SeriesName = string.IsNullOrEmpty(g.Key) ? "WBA" : g.Key,
                Points = down.Select(p => (Timestamp: p.t, Value: p.y)).ToList()
            });
        }

        return result;
    }

    public async Task<IReadOnlyList<WbaTelemetryRecord>> GetWbaTelemetryRecordsAsync(DateTime from, DateTime to)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.MonitorDbContext>();
        return await db.WbaTelemetryRecords
            .Where(r => r.Timestamp >= from && r.Timestamp <= to)
            .OrderBy(r => r.Timestamp)
            .ToListAsync();
    }

    private static double? ExtractWbaMetric(WbaTelemetryRecord r, WbaTrendMetric metric)
    {
        try
        {
            switch (metric)
            {
                case WbaTrendMetric.Pressure:
                    return r.AtmospherePressure;
                case WbaTrendMetric.TempAvg:
                {
                    var arr = JsonSerializer.Deserialize<double[]>(r.TemperaturesJson);
                    if (arr == null || arr.Length == 0) return null;
                    return arr.Average();
                }
                case WbaTrendMetric.Temp0:
                case WbaTrendMetric.Temp1:
                case WbaTrendMetric.Temp2:
                case WbaTrendMetric.Temp3:
                {
                    var arr = JsonSerializer.Deserialize<double[]>(r.TemperaturesJson);
                    var i = metric - WbaTrendMetric.Temp0;
                    return arr != null && i < arr.Length ? arr[i] : null;
                }
                case WbaTrendMetric.Volt0:
                case WbaTrendMetric.Volt1:
                case WbaTrendMetric.Volt2:
                case WbaTrendMetric.Volt3:
                {
                    var arr = JsonSerializer.Deserialize<double[]>(r.VoltagesJson);
                    var i = metric - WbaTrendMetric.Volt0;
                    return arr != null && i < arr.Length ? arr[i] : null;
                }
                default:
                    return null;
            }
        }
        catch
        {
            return null;
        }
    }

    private static List<MeasurementRecord> LttbDownsample(List<MeasurementRecord> data, int threshold)
    {
        if (data.Count <= threshold) return data;

        var sampled = new List<MeasurementRecord>(threshold);
        sampled.Add(data[0]);

        var bucketSize = (double)(data.Count - 2) / (threshold - 2);

        var aIndex = 0;
        for (var i = 0; i < threshold - 2; i++)
        {
            var bucketStart = (int)Math.Floor((i + 1) * bucketSize) + 1;
            var bucketEnd = (int)Math.Floor((i + 2) * bucketSize) + 1;
            if (bucketEnd > data.Count - 1) bucketEnd = data.Count - 1;

            var nextBucketEnd = (int)Math.Floor((i + 3) * bucketSize) + 1;
            if (nextBucketEnd > data.Count - 1) nextBucketEnd = data.Count - 1;
            if (i == threshold - 3) nextBucketEnd = data.Count - 1;

            var nextBucketStart = bucketEnd;
            double avgX = 0, avgY = 0;
            var nextCount = nextBucketEnd - nextBucketStart + 1;
            for (var j = nextBucketStart; j <= nextBucketEnd; j++)
            {
                avgX += data[j].Timestamp.Ticks;
                avgY += data[j].Power;
            }

            avgX /= nextCount;
            avgY /= nextCount;

            double maxArea = -1;
            var maxIndex = bucketStart;
            var pointAx = data[aIndex].Timestamp.Ticks;
            var pointAy = data[aIndex].Power;

            for (var j = bucketStart; j < bucketEnd; j++)
            {
                var area = Math.Abs(
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

    private static List<(DateTime t, double y)> LttbDownsampleDy(IReadOnlyList<(DateTime t, double y)> data, int threshold)
    {
        if (data.Count <= threshold)
            return data.ToList();

        var sampled = new List<(DateTime t, double y)>(threshold);
        sampled.Add(data[0]);

        var bucketSize = (double)(data.Count - 2) / (threshold - 2);

        var aIndex = 0;
        for (var i = 0; i < threshold - 2; i++)
        {
            var bucketStart = (int)Math.Floor((i + 1) * bucketSize) + 1;
            var bucketEnd = (int)Math.Floor((i + 2) * bucketSize) + 1;
            if (bucketEnd > data.Count - 1) bucketEnd = data.Count - 1;

            var nextBucketEnd = (int)Math.Floor((i + 3) * bucketSize) + 1;
            if (nextBucketEnd > data.Count - 1) nextBucketEnd = data.Count - 1;
            if (i == threshold - 3) nextBucketEnd = data.Count - 1;

            var nextBucketStart = bucketEnd;
            double avgX = 0, avgY = 0;
            var nextCount = nextBucketEnd - nextBucketStart + 1;
            for (var j = nextBucketStart; j <= nextBucketEnd; j++)
            {
                avgX += data[j].t.Ticks;
                avgY += data[j].y;
            }

            avgX /= nextCount;
            avgY /= nextCount;

            double maxArea = -1;
            var maxIndex = bucketStart;
            var pointAx = data[aIndex].t.Ticks;
            var pointAy = data[aIndex].y;

            for (var j = bucketStart; j < bucketEnd; j++)
            {
                var area = Math.Abs(
                    (pointAx - avgX) * (data[j].y - pointAy) -
                    (pointAx - data[j].t.Ticks) * (avgY - pointAy));
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
        var totalDeleted = 0;
        var totalWm = 0;

        while (true)
        {
            var batch = await db.MeasurementRecords
                .Where(r => r.Timestamp < cutoff)
                .Take(batchSize)
                .ToListAsync();

            if (batch.Count == 0) break;

            db.MeasurementRecords.RemoveRange(batch);
            await db.SaveChangesAsync();
            totalDeleted += batch.Count;
        }

        while (true)
        {
            var wmBatch = await db.WavelengthMeterSnapshots
                .Where(r => r.Timestamp < cutoff)
                .Take(batchSize)
                .ToListAsync();

            if (wmBatch.Count == 0) break;

            db.WavelengthMeterSnapshots.RemoveRange(wmBatch);
            await db.SaveChangesAsync();
            totalWm += wmBatch.Count;
        }

        if (totalDeleted > 0 || totalWm > 0)
            Log.Information("Data retention: deleted {Count} MeasurementRecords, {Wm} WavelengthMeterSnapshots", totalDeleted,
                totalWm);
    }
}
