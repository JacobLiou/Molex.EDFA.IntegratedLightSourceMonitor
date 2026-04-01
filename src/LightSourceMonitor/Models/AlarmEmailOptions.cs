namespace LightSourceMonitor.Models;

/// <summary>
/// Throttle for HTTP alarm emails: same channel + alarm type is limited to one send per interval while the condition persists.
/// </summary>
public class AlarmEmailOptions
{
    /// <summary>Minimum time between emails for the same channel and <see cref="AlarmType"/>.</summary>
    public int MinIntervalMinutes { get; set; } = 30;
}
