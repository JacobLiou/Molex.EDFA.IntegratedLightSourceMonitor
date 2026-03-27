using System.ComponentModel.DataAnnotations;

namespace LightSourceMonitor.Models;

public class TmsConfig
{
    [Key]
    public int Id { get; set; }
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public int UploadIntervalSec { get; set; } = 300;
    public bool IsEnabled { get; set; }
}
