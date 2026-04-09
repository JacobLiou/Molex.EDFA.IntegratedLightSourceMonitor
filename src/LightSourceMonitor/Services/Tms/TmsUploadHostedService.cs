using LightSourceMonitor.Services.Config;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LightSourceMonitor.Services.Tms;

/// <summary>按 <c>TmsConfig.UploadIntervalSec</c> 周期调用 TMS 批量上传。</summary>
public sealed class TmsUploadHostedService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<TmsUploadHostedService> _logger;

    public TmsUploadHostedService(IServiceProvider services, ILogger<TmsUploadHostedService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TmsUploadHostedService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeSpan.FromSeconds(60);
            try
            {
                using var scope = _services.CreateScope();
                var cfg = scope.ServiceProvider.GetRequiredService<IRuntimeJsonConfigService>();
                var tms = await cfg.LoadTmsAsync(stoppingToken);
                var sec = tms.UploadIntervalSec > 0 ? tms.UploadIntervalSec : 300;
                delay = TimeSpan.FromSeconds(sec);

                if (tms.IsEnabled && !string.IsNullOrWhiteSpace(tms.BaseUrl))
                {
                    var upload = scope.ServiceProvider.GetRequiredService<ITmsService>();
                    await upload.UploadPendingRecordsAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TMS upload cycle failed");
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("TmsUploadHostedService stopped");
    }
}
