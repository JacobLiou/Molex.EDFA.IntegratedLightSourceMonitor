using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LightSourceMonitor.Models;

public class EmailConfig
{
    [Key]
    public int Id { get; set; }
    public string SmtpServer { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public string Username { get; set; } = "";
    public string EncryptedPassword { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string Recipients { get; set; } = "";
    public bool UseSsl { get; set; } = true;
    public AlarmLevel MinAlarmLevel { get; set; } = AlarmLevel.Critical;

    [NotMapped]
    public string ApiUrl
    {
        get => SmtpServer;
        set => SmtpServer = value;
    }
}
