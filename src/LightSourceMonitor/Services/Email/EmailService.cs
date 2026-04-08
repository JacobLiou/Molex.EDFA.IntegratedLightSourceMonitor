using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using LightSourceMonitor.Models;
using LightSourceMonitor.Services.Config;
using LightSourceMonitor.Services.Localization;
using Microsoft.Extensions.Logging;

namespace LightSourceMonitor.Services.Email;

public class EmailService : IEmailService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<EmailService> _logger;
    private readonly IRuntimeJsonConfigService _runtimeJsonConfig;
    private readonly ILanguageService _language;

    public EmailService(
        IHttpClientFactory httpClientFactory,
        ILogger<EmailService> logger,
        IRuntimeJsonConfigService runtimeJsonConfig,
        ILanguageService language)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _runtimeJsonConfig = runtimeJsonConfig;
        _language = language;
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

        var levelText = alarm.Level == AlarmLevel.Critical
            ? _language.GetString("Email_LevelCritical")
            : _language.GetString("Email_LevelWarning");
        var deviceSnText = string.IsNullOrWhiteSpace(deviceSn)
            ? _language.GetString("Email_UnknownDevice")
            : deviceSn.Trim();
        var channelText = string.IsNullOrWhiteSpace(channelName)
            ? _language.GetString("Email_UnknownChannel")
            : channelName.Trim();
        var subject = string.Format(_language.GetString("Email_SubjectFmt"), levelText, alarm.AlarmType);
        var bodyLines = new[]
        {
            string.Format(_language.GetString("Email_Line_Time"),
                alarm.OccurredAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
            string.Format(_language.GetString("Email_Line_Level"), levelText),
            string.Format(_language.GetString("Email_Line_AlarmType"), alarm.AlarmType),
            string.Format(_language.GetString("Email_Line_DevSn"), deviceSnText),
            string.Format(_language.GetString("Email_Line_Channel"), channelText),
            string.Format(_language.GetString("Email_Line_Threshold"), (alarmThreshold ?? 0.15).ToString("F3", CultureInfo.InvariantCulture)),
            string.Format(_language.GetString("Email_Line_Measured"), alarm.MeasuredValue.ToString("F3", CultureInfo.InvariantCulture)),
            string.Format(_language.GetString("Email_Line_Spec"), alarm.SpecValue.ToString("F3", CultureInfo.InvariantCulture)),
            string.Format(_language.GetString("Email_Line_Delta"), alarm.Delta.ToString("F3", CultureInfo.InvariantCulture))
        };
        var bodyLineList = bodyLines.ToList();
        if (specMin.HasValue && specMax.HasValue)
        {
            bodyLineList.Add(string.Format(_language.GetString("Email_Line_Min"), specMin.Value.ToString("F3", CultureInfo.InvariantCulture)));
            bodyLineList.Add(string.Format(_language.GetString("Email_Line_Max"), specMax.Value.ToString("F3", CultureInfo.InvariantCulture)));
        }

        var body = string.Join("<br/>", bodyLineList);

        if (trendScreenshot != null)
        {
            body += "<br/>" + _language.GetString("Email_ScreenshotNote");
        }

        var payload = new
        {
            To = EmailRecipientParser.ToApiToField(config.Recipients),
            Subject = subject,
            Body = body
        };

        var client = _httpClientFactory.CreateClient(nameof(EmailService));
        using var response = await client.PostAsJsonAsync(config.ApiUrl, payload);
        var responseBody = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                string.Format(_language.GetString("Email_ApiFail"),
                    $"{(int)response.StatusCode} {response.ReasonPhrase} {responseBody}".Trim()));
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
        await SendAlarmEmailAsync(testAlarm, "Test channel", "TEST-SN-001", 0.15, -12.200, -11.800);
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
