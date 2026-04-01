using System.Net.Http;
using System.Net.Http.Json;
using LightSourceMonitor.Data;
using LightSourceMonitor.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LightSourceMonitor.Services.Email;

public class EmailService : IEmailService
{
    private readonly IServiceProvider _services;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IServiceProvider services, IHttpClientFactory httpClientFactory, ILogger<EmailService> logger)
    {
        _services = services;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task SendAlarmEmailAsync(AlarmEvent alarm, byte[]? trendScreenshot = null)
    {
        var config = await GetEmailConfigAsync();
        if (config == null || string.IsNullOrWhiteSpace(config.ApiUrl))
        {
            _logger.LogDebug("Email API not configured, skipping alarm email");
            return;
        }

        if (alarm.Level < config.MinAlarmLevel)
            return;

        var levelText = alarm.Level == AlarmLevel.Critical ? "严重告警" : "警告";
        var subject = $"[{levelText}] 集成光源监控 - {alarm.AlarmType}";
        var body = $"""
时间: {alarm.OccurredAt:yyyy-MM-dd HH:mm:ss}
等级: {levelText}
告警类型: {alarm.AlarmType}
通道ID: {alarm.ChannelId}
测量值: {alarm.MeasuredValue:F3}
Spec值: {alarm.SpecValue:F3}
偏差: {alarm.Delta:F3}
""";

        if (trendScreenshot != null)
        {
            body += Environment.NewLine + "趋势截图已生成，但当前邮件 API 版本不支持附件。";
        }

        var payload = new
        {
            To = string.Join(",", config.Recipients.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)),
            Subject = subject,
            Body = body
        };

        var client = _httpClientFactory.CreateClient(nameof(EmailService));
        using var response = await client.PostAsJsonAsync(config.ApiUrl, payload);
        var responseBody = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"邮件 API 调用失败: {(int)response.StatusCode} {response.ReasonPhrase} {responseBody}".Trim());
        }

        _logger.LogInformation("Alarm email sent by HTTP API for channel {ChannelId}", alarm.ChannelId);
    }

    public async Task SendTestEmailAsync()
    {
        var testAlarm = new AlarmEvent
        {
            OccurredAt = DateTime.Now,
            AlarmType = AlarmType.PowerDrift,
            Level = AlarmLevel.Critical,
            MeasuredValue = -12.345,
            SpecValue = -12.000,
            Delta = 0.345,
            ChannelId = 0
        };
        await SendAlarmEmailAsync(testAlarm);
    }

    private async Task<EmailConfig?> GetEmailConfigAsync()
    {
        try
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();
            return await db.EmailConfigs.FirstOrDefaultAsync();
        }
        catch
        {
            return null;
        }
    }
}
