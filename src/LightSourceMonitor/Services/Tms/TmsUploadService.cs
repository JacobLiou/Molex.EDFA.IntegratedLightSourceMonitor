using System.Net.Http;
using System.Net.Http.Json;
using LightSourceMonitor.Data;
using LightSourceMonitor.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LightSourceMonitor.Services.Tms;

public class TmsUploadService : ITmsService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<TmsUploadService> _logger;
    private readonly HttpClient _httpClient;

    public TmsUploadService(IServiceProvider services, ILogger<TmsUploadService> logger)
    {
        _services = services;
        _logger = logger;
        _httpClient = new HttpClient();
    }

    public async Task UploadPendingRecordsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();
        var tmsConfig = await db.TmsConfigs.FirstOrDefaultAsync(cancellationToken);

        if (tmsConfig == null || !tmsConfig.IsEnabled || string.IsNullOrWhiteSpace(tmsConfig.BaseUrl))
            return;

        var pending = await db.MeasurementRecords
            .Where(r => !r.IsSyncedToTms)
            .OrderBy(r => r.Timestamp)
            .Take(500)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0) return;

        try
        {
            _httpClient.DefaultRequestHeaders.Clear();
            if (!string.IsNullOrEmpty(tmsConfig.ApiKey))
                _httpClient.DefaultRequestHeaders.Add("X-API-Key", tmsConfig.ApiKey);

            var payload = pending.Select(r => new
            {
                r.ChannelId,
                Timestamp = r.Timestamp.ToString("o"),
                r.Power,
                r.Wavelength
            });

            var response = await _httpClient.PostAsJsonAsync(
                $"{tmsConfig.BaseUrl.TrimEnd('/')}/measurements",
                payload,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                foreach (var record in pending)
                    record.IsSyncedToTms = true;
                await db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Uploaded {Count} records to TMS", pending.Count);
            }
            else
            {
                _logger.LogWarning("TMS upload failed: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TMS upload error");
        }
    }

    public async Task<bool> TestConnectionAsync()
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();
        var tmsConfig = await db.TmsConfigs.FirstOrDefaultAsync();

        if (tmsConfig == null || string.IsNullOrWhiteSpace(tmsConfig.BaseUrl))
            return false;

        try
        {
            _httpClient.DefaultRequestHeaders.Clear();
            if (!string.IsNullOrEmpty(tmsConfig.ApiKey))
                _httpClient.DefaultRequestHeaders.Add("X-API-Key", tmsConfig.ApiKey);

            var response = await _httpClient.GetAsync($"{tmsConfig.BaseUrl.TrimEnd('/')}/health");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TMS connection test failed");
            return false;
        }
    }
}
