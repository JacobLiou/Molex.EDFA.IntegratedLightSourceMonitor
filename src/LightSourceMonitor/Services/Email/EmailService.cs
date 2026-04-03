using System.Net.Http;
using System.Net.Http.Json;
using LightSourceMonitor.Models;
using LightSourceMonitor.Services.Config;
using Microsoft.Extensions.Logging;

namespace LightSourceMonitor.Services.Email;

public class EmailService : IEmailService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<EmailService> _logger;
    private readonly IRuntimeJsonConfigService _runtimeJsonConfig;

    public EmailService(
        IHttpClientFactory httpClientFactory,
        ILogger<EmailService> logger,
        IRuntimeJsonConfigService runtimeJsonConfig)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _runtimeJsonConfig = runtimeJsonConfig;
    }

    public async Task SendAlarmEmailAsync(
        AlarmEvent alarm,
        string? channelName = null,
        string? deviceSn = null,
        double? alarmThreshold = null,
        double? specMin = null,
        double? specMax = null,
        byte[]? trendScreenshot = null)
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
        var deviceSnText = string.IsNullOrWhiteSpace(deviceSn) ? "未知设备" : deviceSn.Trim();
        var channelText = string.IsNullOrWhiteSpace(channelName) ? "未知通道" : channelName.Trim();
        var subject = $"[{levelText}] 集成光源监控 - {alarm.AlarmType}";
        var bodyLines = new[]
        {
            $"时间: {alarm.OccurredAt:yyyy-MM-dd HH:mm:ss}",
            $"等级: {levelText}",
            $"告警类型: {alarm.AlarmType}",
            $"设备SN: {deviceSnText}",
            $"通道名称: {channelText}",
            $"统一告警阈值: {(alarmThreshold ?? 0.15):F3}",
            $"测量值: {alarm.MeasuredValue:F3}",
            $"Spec值: {alarm.SpecValue:F3}",
            $"Delta值: {alarm.Delta:F3}"
        };
        var bodyLineList = bodyLines.ToList();
        if (specMin.HasValue && specMax.HasValue)
        {
            bodyLineList.Add($"下限: {specMin.Value:F3}");
            bodyLineList.Add($"上限: {specMax.Value:F3}");
        }

        var body = string.Join("<br/>", bodyLineList);

        if (trendScreenshot != null)
        {
            body += "<br/>趋势截图已生成，但当前邮件 API 版本不支持附件。";
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

        _logger.LogInformation("Alarm email sent by HTTP API for channel {ChannelName} ({ChannelId})", channelText, alarm.ChannelId);
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
        await SendAlarmEmailAsync(testAlarm, "测试通道", "TEST-SN-001", 0.15, -12.200, -11.800);
    }

    private async Task<EmailConfig?> GetEmailConfigAsync()
    {
        try
        {
            return await _runtimeJsonConfig.LoadEmailAsync();
        }
        catch
        {
            return null;
        }
    }
}
