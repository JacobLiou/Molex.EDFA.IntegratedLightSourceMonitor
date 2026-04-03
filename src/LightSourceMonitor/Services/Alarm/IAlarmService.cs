using LightSourceMonitor.Models;

namespace LightSourceMonitor.Services.Alarm;

public interface IAlarmService
{
    event Action<AlarmEvent>? AlarmRaised;
    Task EvaluateAsync(MeasurementRecord record, LaserChannel channel);

    /// <summary>WM 波长服务多路表格：与 <see cref="WavelengthServiceChannelSpec"/> 比较并可能产生 <see cref="AlarmType.WavelengthDrift"/>。</summary>
    Task EvaluateWavelengthServiceChannelAsync(
        DateTime occurredAt,
        int wmChannelIndex,
        double measuredWavelengthNm,
        WavelengthServiceChannelSpec spec,
        WavelengthServiceSettings wmSettings,
        string queryDeviceId);
}
