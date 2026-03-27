namespace LightSourceMonitor.Models;

public class AppSettings
{
    public int SamplingIntervalMs { get; set; } = 2000;
    public int WmSweepEveryNSamples { get; set; } = 100;
    public int DbWriteEveryNSamples { get; set; } = 10;
    public int DataRetentionDays { get; set; } = 30;
    public double DefaultPdAlarmDelta { get; set; } = 0.15;
    public double DefaultWmAlarmDelta { get; set; } = 0.05;
}
