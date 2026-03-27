using LightSourceMonitor.Models;

namespace LightSourceMonitor.Services.Email;

public interface IEmailService
{
    Task SendAlarmEmailAsync(AlarmEvent alarm, byte[]? trendScreenshot = null);
    Task SendTestEmailAsync();
}
