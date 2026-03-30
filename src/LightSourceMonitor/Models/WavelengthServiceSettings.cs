namespace LightSourceMonitor.Models;

public class WavelengthServiceSettings
{
    public string Host { get; set; } = "172.16.148.124";
    public int Port { get; set; } = 8003;
    public int TimeoutMs { get; set; } = 5000;
    public bool IsSimulated { get; set; } = true;
    public double SimulatedWavelengthMin { get; set; } = 1310.0;
    public double SimulatedWavelengthMax { get; set; } = 1312.0;
    public double SimulatedPowerMin { get; set; } = -10.0;
    public double SimulatedPowerMax { get; set; } = -7.5;
}
