using LightSourceMonitor.Models;

namespace LightSourceMonitor.Services.Alarm;

public interface IAlarmService
{
    event Action<AlarmEvent>? AlarmRaised;
    Task EvaluateAsync(MeasurementRecord record, LaserChannel channel);
}
