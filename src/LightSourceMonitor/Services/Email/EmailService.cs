using LightSourceMonitor.Data;
using LightSourceMonitor.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace LightSourceMonitor.Services.Email;

public class EmailService : IEmailService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IServiceProvider services, ILogger<EmailService> logger)
    {
        _services = services;
        _logger = logger;
    }

    public async Task SendAlarmEmailAsync(AlarmEvent alarm, byte[]? trendScreenshot = null)
    {
        var config = await GetEmailConfigAsync();
        if (config == null || string.IsNullOrWhiteSpace(config.SmtpServer))
        {
            _logger.LogDebug("Email not configured, skipping alarm email");
            return;
        }

        if (alarm.Level < config.MinAlarmLevel)
            return;

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("光源监控系统", config.FromAddress));

        foreach (var addr in config.Recipients.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            message.To.Add(MailboxAddress.Parse(addr));

        var levelText = alarm.Level == AlarmLevel.Critical ? "严重告警" : "警告";
        message.Subject = $"[{levelText}] 集成光源监控 — {alarm.AlarmType}";

        var builder = new BodyBuilder
        {
            HtmlBody = $"""
                <html><body style="font-family:sans-serif; background:#1B1B2F; color:#E8E8F0; padding:20px;">
                <h2 style="color:{(alarm.Level == AlarmLevel.Critical ? "#FF1744" : "#FFAB00")}">
                    {levelText}: {alarm.AlarmType}
                </h2>
                <table style="border-collapse:collapse; margin:16px 0;">
                    <tr><td style="padding:4px 12px; color:#9898B0;">通道ID</td><td style="padding:4px 12px;">{alarm.ChannelId}</td></tr>
                    <tr><td style="padding:4px 12px; color:#9898B0;">时间</td><td style="padding:4px 12px;">{alarm.OccurredAt:yyyy-MM-dd HH:mm:ss}</td></tr>
                    <tr><td style="padding:4px 12px; color:#9898B0;">测量值</td><td style="padding:4px 12px; font-family:Consolas;">{alarm.MeasuredValue:F3}</td></tr>
                    <tr><td style="padding:4px 12px; color:#9898B0;">Spec值</td><td style="padding:4px 12px; font-family:Consolas;">{alarm.SpecValue:F3}</td></tr>
                    <tr><td style="padding:4px 12px; color:#9898B0;">偏差</td><td style="padding:4px 12px; font-family:Consolas; color:#FF1744;">{alarm.Delta:F3}</td></tr>
                </table>
                <p style="color:#55556A; font-size:12px;">此邮件由集成光源监控系统自动发送</p>
                </body></html>
                """
        };

        if (trendScreenshot != null)
        {
            builder.Attachments.Add("trend.png", trendScreenshot, new ContentType("image", "png"));
        }

        message.Body = builder.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(config.SmtpServer, config.SmtpPort,
            config.UseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto);

        if (!string.IsNullOrEmpty(config.Username))
            await client.AuthenticateAsync(config.Username, config.EncryptedPassword);

        await client.SendAsync(message);
        await client.DisconnectAsync(true);

        _logger.LogInformation("Alarm email sent for channel {ChannelId}", alarm.ChannelId);
    }

    public async Task SendTestEmailAsync()
    {
        var testAlarm = new AlarmEvent
        {
            OccurredAt = DateTime.Now,
            AlarmType = AlarmType.PowerDrift,
            Level = AlarmLevel.Warning,
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
