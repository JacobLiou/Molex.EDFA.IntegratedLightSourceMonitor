using System.ComponentModel.DataAnnotations;

namespace LightSourceMonitor.Models;

/// <summary>一次波长计整表采集快照（多路波长/功率以逗号分隔，无效路为空段）。</summary>
public class WavelengthMeterSnapshot
{
    [Key]
    public long Id { get; set; }

    public DateTime Timestamp { get; set; }

    [MaxLength(128)]
    public string QueryDeviceId { get; set; } = "";

    /// <summary>按路序 0..N-1 的波长 (nm)，逗号分隔；无效路为空。</summary>
    public string OneTimeValues { get; set; } = "";

    /// <summary>按路序 0..N-1 的功率 (dBm)，逗号分隔；无效路为空。</summary>
    public string PowerValues { get; set; } = "";
}
