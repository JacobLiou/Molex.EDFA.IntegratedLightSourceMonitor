using System.ComponentModel.DataAnnotations;

namespace LightSourceMonitor.Models;

public class MeasurementRecord
{
    [Key]
    public long Id { get; set; }
    public int ChannelId { get; set; }
    public DateTime Timestamp { get; set; }
    public double Power { get; set; }
    public double Wavelength { get; set; }
    public bool IsUploadToTms { get; set; }
}
