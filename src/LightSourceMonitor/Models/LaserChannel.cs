namespace LightSourceMonitor.Models;

public class LaserChannel
{
    public int Id { get; set; }
    public string DeviceSN { get; set; } = "";
    public int ChannelIndex { get; set; }
    public string ChannelName { get; set; } = "";
    public double SpecWavelength { get; set; }
    public double SpecPowerMin { get; set; }
    public double SpecPowerMax { get; set; }
    public double AlarmDelta { get; set; } = 0.15;
    public bool IsEnabled { get; set; } = true;
}
