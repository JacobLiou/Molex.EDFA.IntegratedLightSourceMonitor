namespace LightSourceMonitor.Models;

public class DriverSettings
{
    // Supported values: Simulated, Hardware
    public string Mode { get; set; } = "Simulated";

    // Argument used by PD driver Open(...).
    public string PdOpenArgument { get; set; } = "SIM";

    // WM configuration XML path, e.g. .\\config\\UDL_WM.xml
    public string WmConfigXmlPath { get; set; } = "";

    // Optional multi-device configuration. If empty, single-device fallback is used.
    public List<PdDeviceSettings> Devices { get; set; } = new();

    public List<PdDeviceSettings> GetEffectiveDevices()
    {
        var configured = Devices
            .Where(d => d.Enabled)
            .ToList();

        if (configured.Count > 0)
            return configured;

        var fallbackDeviceSn = string.Equals(Mode, "Simulated", StringComparison.OrdinalIgnoreCase)
            ? "SIM-PD-001"
            : "PD-001";

        return
        [
            new PdDeviceSettings
            {
                DeviceSN = fallbackDeviceSn,
                UsbAddress = string.IsNullOrWhiteSpace(PdOpenArgument) ? "SIM" : PdOpenArgument,
                WmConfigXmlPath = WmConfigXmlPath,
                Enabled = true
            }
        ];
    }
}

public class PdDeviceSettings
{
    public string DeviceSN { get; set; } = "";
    public string UsbAddress { get; set; } = "";
    public string WmConfigXmlPath { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public List<PdChannelSettings> Channels { get; set; } = new();
}

public class PdChannelSettings
{
    public int ChannelIndex { get; set; }
    public string ChannelName { get; set; } = "";
    public double SpecWavelength { get; set; }
    public double SpecPowerMin { get; set; }
    public double SpecPowerMax { get; set; }
    public double AlarmDelta { get; set; } = 0.15;
    public bool IsEnabled { get; set; } = true;
}
