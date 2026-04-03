namespace LightSourceMonitor.Models;

/// <summary>邮件 HTTP API 配置，对应 <c>config/EmailConfig.json</c>。</summary>
public class EmailConfig
{
    public string ApiUrl { get; set; } = "";
    public string Recipients { get; set; } = "";
    public AlarmLevel MinAlarmLevel { get; set; } = AlarmLevel.Critical;
}
