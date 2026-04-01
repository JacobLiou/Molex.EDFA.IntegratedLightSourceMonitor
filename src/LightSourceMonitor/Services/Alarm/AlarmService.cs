using System.Collections.Concurrent;
using LightSourceMonitor.Data;
using LightSourceMonitor.Helpers;
using LightSourceMonitor.Models;
using LightSourceMonitor.Services.Email;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LightSourceMonitor.Services.Alarm;

public class AlarmService : IAlarmService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<AlarmService> _logger;
    private readonly IEmailService _emailService;
    private readonly ConcurrentDictionary<string, DateTime> _lastEmailSent = new();

    public event Action<AlarmEvent>? AlarmRaised;

    public AlarmService(IServiceProvider services, ILogger<AlarmService> logger, IEmailService emailService)
    {
        _services = services;
        _logger = logger;
        _emailService = emailService;
    }

    public async Task EvaluateAsync(MeasurementRecord record, LaserChannel channel)
    {
        var powerDelta = Math.Abs(record.Power - (channel.SpecPowerMin + channel.SpecPowerMax) / 2.0);
        // Acquisition stores SpecWavelength in record.Wavelength; WM live readings are on the overview table only.
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

        if (alarm == null) return;

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

        await TrySendEmailAsync(alarm, channel);
    }

    private async Task TrySendEmailAsync(AlarmEvent alarm, LaserChannel channel)
    {
        var key = $"{alarm.ChannelId}_{alarm.AlarmType}";
        if (_lastEmailSent.TryGetValue(key, out var lastSent) &&
            DateTime.Now - lastSent < TimeSpan.FromHours(1))
        {
            return;
        }

        try
        {
            await _emailService.SendAlarmEmailAsync(alarm);
            alarm.EmailSent = true;
            _lastEmailSent[key] = DateTime.Now;

            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();
            db.AlarmEvents.Update(alarm);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send alarm email");
        }
    }
}
