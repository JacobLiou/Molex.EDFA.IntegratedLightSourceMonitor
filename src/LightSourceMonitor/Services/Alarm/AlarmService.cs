using LightSourceMonitor.Data;
using LightSourceMonitor.Helpers;
using LightSourceMonitor.Models;
using LightSourceMonitor.Services.Email;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace LightSourceMonitor.Services.Alarm;

public class AlarmService : IAlarmService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<AlarmService> _logger;
    private readonly IEmailService _emailService;
    private readonly AlarmEmailOptions _emailOptions;
    private readonly ConcurrentDictionary<string, DateTime> _lastEmailSentUtc = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _emailSendGate = new(1, 1);

    public event Action<AlarmEvent>? AlarmRaised;

    public AlarmService(
        IServiceProvider services,
        ILogger<AlarmService> logger,
        IEmailService emailService,
        IOptions<AlarmEmailOptions> emailOptions)
    {
        _services = services;
        _logger = logger;
        _emailService = emailService;
        _emailOptions = emailOptions.Value;
    }

    public async Task EvaluateAsync(MeasurementRecord record, LaserChannel channel)
    {
        var powerDelta = Math.Abs(record.Power - (channel.SpecPowerMin + channel.SpecPowerMax) / 2.0);
        var wlDelta = channel.SpecWavelength > 0
            ? Math.Abs(record.Wavelength - channel.SpecWavelength)
            : 0;

        AlarmEvent? alarm = null;

        if (powerDelta > channel.AlarmDelta && channel.AlarmDelta > 0)
        {
            var level = powerDelta > channel.AlarmDelta * 1.5 ? AlarmLevel.Critical : AlarmLevel.Warning;
            alarm = new AlarmEvent
            {
                ChannelId = channel.Id,
                OccurredAt = record.Timestamp,
                AlarmType = AlarmType.PowerDrift,
                Level = level,
                MeasuredValue = record.Power,
                SpecValue = (channel.SpecPowerMin + channel.SpecPowerMax) / 2.0,
                Delta = powerDelta
            };
        }
        else if (wlDelta > 0.05 && channel.SpecWavelength > 0)
        {
            var level = wlDelta > 0.1 ? AlarmLevel.Critical : AlarmLevel.Warning;
            alarm = new AlarmEvent
            {
                ChannelId = channel.Id,
                OccurredAt = record.Timestamp,
                AlarmType = AlarmType.WavelengthDrift,
                Level = level,
                MeasuredValue = record.Wavelength,
                SpecValue = channel.SpecWavelength,
                Delta = wlDelta
            };
        }

        if (alarm == null)
        {
            ClearEmailThrottleForChannel(channel.Id);
            return;
        }

        try
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();
            db.AlarmEvents.Add(alarm);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save alarm event");
        }

        AlarmRaised.SafeInvoke(alarm, nameof(AlarmRaised));
        _logger.LogWarning("Alarm: {Type} on {Channel} — measured={Value:F3}, spec={Spec:F3}, delta={Delta:F3}",
            alarm.AlarmType, channel.ChannelName, alarm.MeasuredValue, alarm.SpecValue, alarm.Delta);

        _ = TrySendEmailAsync(alarm, channel);
    }

    private void ClearEmailThrottleForChannel(int channelId)
    {
        _lastEmailSentUtc.TryRemove(EmailThrottleKey(channelId, AlarmType.PowerDrift), out _);
        _lastEmailSentUtc.TryRemove(EmailThrottleKey(channelId, AlarmType.WavelengthDrift), out _);
    }

    private static string EmailThrottleKey(int channelId, AlarmType type) => $"{channelId}_{type}";

    private TimeSpan GetMinEmailInterval()
    {
        var m = _emailOptions.MinIntervalMinutes;
        if (m < 30) m = 30;
        if (m > 24 * 60) m = 24 * 60;
        return TimeSpan.FromMinutes(m);
    }

    private async Task TrySendEmailAsync(AlarmEvent alarm, LaserChannel channel)
    {
        if (!await _emailSendGate.WaitAsync(0))
        {
            _logger.LogDebug("Alarm email skipped: another send task is in progress");
            return;
        }

        var key = EmailThrottleKey(alarm.ChannelId, alarm.AlarmType);
        var interval = GetMinEmailInterval();
        var nowUtc = DateTime.UtcNow;

        try
        {
            if (_lastEmailSentUtc.TryGetValue(key, out var lastSentUtc) && nowUtc - lastSentUtc < interval)
            {
                _logger.LogDebug(
                    "Alarm email suppressed (debounce): key={Key}, last={Last:O}, interval={Interval}",
                    key, lastSentUtc, interval);
                return;
            }

            // Reserve before await so concurrent background tasks cannot send duplicate mails.
            _lastEmailSentUtc[key] = nowUtc;

            await _emailService.SendAlarmEmailAsync(alarm);
            alarm.EmailSent = true;

            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();
            db.AlarmEvents.Update(alarm);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send alarm email");
            // Keep throttle reservation to avoid hammering a failing API; next window opens after interval.
        }
        finally
        {
            _emailSendGate.Release();
        }
    }
}
