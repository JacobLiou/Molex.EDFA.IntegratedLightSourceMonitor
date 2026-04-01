using LightSourceMonitor.Models;

namespace LightSourceMonitor.Services.Email;

public interface IEmailService
{
    Task SendAlarmEmailAsync(
        AlarmEvent alarm,
        string? channelName = null,
        double? specMin = null,
        double? specMax = null,
        byte[]? trendScreenshot = null);
    Task SendTestEmailAsync();
}
