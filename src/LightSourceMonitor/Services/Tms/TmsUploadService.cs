using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using LightSourceMonitor.Data;
using LightSourceMonitor.Models;
using LightSourceMonitor.Services.Config;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LightSourceMonitor.Services.Tms;

public class TmsUploadService : ITmsService
{
    private const int BatchSize = 500;

    private readonly IServiceProvider _services;
    private readonly ILogger<TmsUploadService> _logger;
    private readonly HttpClient _httpClient;
    private readonly IRuntimeJsonConfigService _runtimeJsonConfig;

    public TmsUploadService(
        IServiceProvider services,
        ILogger<TmsUploadService> logger,
        IRuntimeJsonConfigService runtimeJsonConfig)
    {
        _services = services;
        _logger = logger;
        _httpClient = new HttpClient();
        _runtimeJsonConfig = runtimeJsonConfig;
    }

    public async Task UploadPendingRecordsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();
        var tmsConfig = await _runtimeJsonConfig.LoadTmsAsync(cancellationToken);

        if (!tmsConfig.IsEnabled || string.IsNullOrWhiteSpace(tmsConfig.BaseUrl))
            return;

        var baseUrl = tmsConfig.BaseUrl.TrimEnd('/');

        void ApplyHeaders()
        {
            _httpClient.DefaultRequestHeaders.Clear();
            if (!string.IsNullOrEmpty(tmsConfig.ApiKey))
                _httpClient.DefaultRequestHeaders.Add("X-API-Key", tmsConfig.ApiKey);
        }

        ApplyHeaders();

        var pdPending = await db.MeasurementRecords
            .Where(r => !r.IsUploadToTms)
            .OrderBy(r => r.Timestamp)
            .ThenBy(r => r.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (pdPending.Count > 0)
        {
            try
            {
                var payload = pdPending.Select(r => new
                {
                    r.ChannelId,
                    Timestamp = r.Timestamp.ToString("o"),
                    r.Power,
                    r.Wavelength
                });
                var rel = string.IsNullOrWhiteSpace(tmsConfig.MeasurementsPath)
                    ? "measurements"
                    : tmsConfig.MeasurementsPath.TrimStart('/');
                var response = await _httpClient.PostAsJsonAsync($"{baseUrl}/{rel}", payload, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    foreach (var r in pdPending)
                        r.IsUploadToTms = true;
                    await db.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation("TMS uploaded {Count} PD records", pdPending.Count);
                }
                else
                    _logger.LogWarning("TMS PD upload failed: {StatusCode}", response.StatusCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TMS PD upload error");
            }
        }

        ApplyHeaders();

        var wmPending = await db.WavelengthMeterSnapshots
            .Where(r => !r.IsUploadToTms)
            .OrderBy(r => r.Timestamp)
            .ThenBy(r => r.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (wmPending.Count > 0)
        {
            try
            {
                var payload = wmPending.Select(r => new
                {
                    r.QueryDeviceId,
                    Timestamp = r.Timestamp.ToString("o"),
                    r.OneTimeValues,
                    r.PowerValues
                });
                var rel = string.IsNullOrWhiteSpace(tmsConfig.WavelengthSnapshotsPath)
                    ? "wavelength-snapshots"
                    : tmsConfig.WavelengthSnapshotsPath.TrimStart('/');
                var response = await _httpClient.PostAsJsonAsync($"{baseUrl}/{rel}", payload, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    foreach (var r in wmPending)
                        r.IsUploadToTms = true;
                    await db.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation("TMS uploaded {Count} WM snapshots", wmPending.Count);
                }
                else
                    _logger.LogWarning("TMS WM upload failed: {StatusCode}", response.StatusCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TMS WM upload error");
            }
        }

        ApplyHeaders();

        var wbaPending = await db.WbaTelemetryRecords
            .Where(r => !r.IsUploadToTms)
            .OrderBy(r => r.Timestamp)
            .ThenBy(r => r.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (wbaPending.Count > 0)
        {
            try
            {
                var payload = wbaPending.Select(r => new
                {
                    DeviceSN = r.DeviceSN,
                    Timestamp = r.Timestamp.ToString("o"),
                    Voltages = JsonSerializer.Deserialize<double[]>(r.VoltagesJson) ?? Array.Empty<double>(),
                    Temperatures = JsonSerializer.Deserialize<double[]>(r.TemperaturesJson) ?? Array.Empty<double>(),
                    r.AtmospherePressure
                });
                var rel = string.IsNullOrWhiteSpace(tmsConfig.WbaTelemetryPath)
                    ? "wba-telemetry"
                    : tmsConfig.WbaTelemetryPath.TrimStart('/');
                var response = await _httpClient.PostAsJsonAsync($"{baseUrl}/{rel}", payload, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    foreach (var r in wbaPending)
                        r.IsUploadToTms = true;
                    await db.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation("TMS uploaded {Count} WBA records", wbaPending.Count);
                }
                else
                    _logger.LogWarning("TMS WBA upload failed: {StatusCode}", response.StatusCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TMS WBA upload error");
            }
        }
    }

    public async Task<bool> TestConnectionAsync()
    {
        var tmsConfig = await _runtimeJsonConfig.LoadTmsAsync();

        if (string.IsNullOrWhiteSpace(tmsConfig.BaseUrl))
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
