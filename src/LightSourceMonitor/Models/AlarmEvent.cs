using System.ComponentModel.DataAnnotations;

namespace LightSourceMonitor.Models;

public enum AlarmType
{
    PowerDrift,
    WavelengthDrift,
    DeviceOffline
}

public enum AlarmLevel
{
    Warning,
    Critical
}

public class AlarmEvent
{
    [Key]
    public long Id { get; set; }
    public int ChannelId { get; set; }
    public DateTime OccurredAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public AlarmType AlarmType { get; set; }
    public AlarmLevel Level { get; set; }
    public double MeasuredValue { get; set; }
    public double SpecValue { get; set; }
    public double Delta { get; set; }
    public bool EmailSent { get; set; }
}
