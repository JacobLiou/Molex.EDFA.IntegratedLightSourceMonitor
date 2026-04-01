using System.ComponentModel.DataAnnotations;

namespace LightSourceMonitor.Models;

public class AcquisitionConfig
{
    [Key]
    public int Id { get; set; }
    public int SamplingIntervalMs { get; set; } = 5000;
    public int WmSweepEveryN { get; set; } = 20;
    public int DbWriteEveryN { get; set; } = 20;
}
