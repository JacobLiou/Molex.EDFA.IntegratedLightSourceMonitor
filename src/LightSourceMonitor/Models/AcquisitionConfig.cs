namespace LightSourceMonitor.Models;

/// <summary>采集周期与落库节奏，对应 <c>config/AcquisitionConfig.json</c>。</summary>
public class AcquisitionConfig
{
    public int SamplingIntervalMs { get; set; } = 5000;
    public int WmSweepEveryN { get; set; } = 20;
    /// <summary>每 N 次采集周期执行一次 WBA 读数；1 表示与 PD 同频。</summary>
    public int WbaSweepEveryN { get; set; } = 1;
    public int DbWriteEveryN { get; set; } = 20;
}
