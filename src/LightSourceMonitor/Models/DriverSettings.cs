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

    public DriverSettingsValidationResult ValidateConfiguration()
    {
        var result = new DriverSettingsValidationResult();
        var devices = GetEffectiveDevices();

        if (devices.Count == 0)
        {
            result.Errors.Add("Driver.Devices 未配置任何启用设备，且旧版回退配置不可用。");
            return result;
        }

        var duplicateDeviceSn = devices
            .GroupBy(d => d.DeviceSN, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        foreach (var deviceSn in duplicateDeviceSn)
            result.Errors.Add($"存在重复 DeviceSN: {deviceSn}");

        var duplicateUsbAddress = devices
            .Where(d => !string.IsNullOrWhiteSpace(d.UsbAddress))
            .GroupBy(d => d.UsbAddress, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        foreach (var usbAddress in duplicateUsbAddress)
            result.Errors.Add($"存在重复 UsbAddress: {usbAddress}");

        var totalEnabledChannels = 0;

        foreach (var device in devices)
        {
            if (string.IsNullOrWhiteSpace(device.DeviceSN))
                result.Errors.Add("存在空 DeviceSN 配置。每个设备都必须配置 DeviceSN。");

            if (string.IsNullOrWhiteSpace(device.UsbAddress))
                result.Errors.Add($"设备 {device.DeviceSN} 的 UsbAddress 为空。");

            var enabledChannels = device.Channels
                .Where(c => c.IsEnabled)
                .ToList();
            totalEnabledChannels += enabledChannels.Count;

            if (device.Channels.Count == 0)
                result.Warnings.Add($"设备 {device.DeviceSN} 未配置任何 Channels。该设备不会显示任何通道。");

            if (enabledChannels.Count == 0)
                result.Warnings.Add($"设备 {device.DeviceSN} 没有启用通道。该设备不会参与采集。");

            var duplicateChannelIndex = enabledChannels
                .GroupBy(c => c.ChannelIndex)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .OrderBy(i => i)
                .ToList();
            foreach (var channelIndex in duplicateChannelIndex)
                result.Errors.Add($"设备 {device.DeviceSN} 存在重复 ChannelIndex: {channelIndex}");
        }

        if (totalEnabledChannels == 0)
            result.Errors.Add("所有设备的启用通道数为 0，采集无法启动。");

        return result;
    }
}

public class DriverSettingsValidationResult
{
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public bool IsValid => Errors.Count == 0;
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
