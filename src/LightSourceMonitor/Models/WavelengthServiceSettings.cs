namespace LightSourceMonitor.Models;

public class WavelengthServiceSettings
{
    /// <summary>
    /// Driver mode: Socket | SimulatedCom
    /// </summary>
    public string Mode { get; set; } = "Socket";

    /// <summary>
    /// COM port name used by simulated COM driver.
    /// </summary>
    public string ComPort { get; set; } = "COM9";

    /// <summary>
    /// COM baud rate used by simulated COM driver.
    /// </summary>
    public int BaudRate { get; set; } = 115200;

    public string Host { get; set; } = "172.16.148.124";
    public int Port { get; set; } = 8003;
    public int TimeoutMs { get; set; } = 5000;
    public int Timeout { get; set; } = 5000;

    /// <summary>First argument to wavelength service QUERY (device id). Empty = first PD DeviceSN from Driver.Devices.</summary>
    public string QueryDeviceId { get; set; } = "";

    /// <summary>Number of WM channel indices 0..N-1 to query for the overview table.</summary>
    public int TableChannelCount { get; set; } = 8;

    /// <summary>各路 AlarmDeltaNm 为 0 时使用的默认波长容差 (nm)。</summary>
    public double DefaultWavelengthAlarmDeltaNm { get; set; } = 0.05;

    /// <summary>多路 WM 表格每路的标称波长与告警容差；为空则不触发 WM 波长告警。</summary>
    public List<WavelengthServiceChannelSpec> ChannelSpecs { get; set; } = new();

    public bool IsSimulated { get; set; } = true;
    public double SimulatedWavelengthMin { get; set; } = 1310.0;
    public double SimulatedWavelengthMax { get; set; } = 1312.0;
    public double SimulatedPowerMin { get; set; } = -10.0;
    public double SimulatedPowerMax { get; set; } = -7.5;

    /// <summary>Per-read delay for simulated COM response (ms).</summary>
    public int SimulatedComReadDelayMs { get; set; } = 20;

    /// <summary>Channel-to-channel wavelength spacing in nm for simulated COM mode.</summary>
    public double SimulatedChannelSpacingNm { get; set; } = 0.05;

    /// <summary>Random wavelength jitter amplitude in nm for simulated COM mode.</summary>
    public double SimulatedComNoiseNm { get; set; } = 0.01;

    /// <summary>Random power jitter amplitude in dBm for simulated COM mode.</summary>
    public double SimulatedComNoiseDbm { get; set; } = 0.3;

    public int GetEffectiveTimeoutMs() => TimeoutMs > 0 ? TimeoutMs : Timeout;

    public WavelengthServiceChannelSpec? ResolveChannelSpec(int channelIndex)
    {
        var list = ChannelSpecs;
        if (list == null || list.Count == 0)
            return null;

        var exact = list.FirstOrDefault(x => x.ChannelIndex == channelIndex);
        if (exact != null)
            return exact;

        if (channelIndex >= 0 && channelIndex < list.Count)
            return list[channelIndex];

        return null;
    }

    public double GetEffectiveAlarmDeltaNm(WavelengthServiceChannelSpec spec)
    {
        if (spec.AlarmDeltaNm > 0)
            return spec.AlarmDeltaNm;

        return DefaultWavelengthAlarmDeltaNm > 0 ? DefaultWavelengthAlarmDeltaNm : 0.05;
    }
}
