namespace LightSourceMonitor.Services.Localization;

/// <summary>与 Strings.*.xaml 中 x:Key 一致的常量，供 ViewModel 逻辑分支使用。</summary>
public static class UiResourceKeys
{
    public const string TimeRange1H = "TimeRange_1H";
    public const string TimeRange24H = "TimeRange_24H";
    public const string TimeRange7D = "TimeRange_7D";
    public const string TimeRange30D = "TimeRange_30D";

    public const string TimeRangeAll = "TimeRange_All";

    public const string AlarmLevelAll = "Alarm_Filter_All";
    public const string AlarmLevelCritical = "Alarm_Filter_Critical";
    public const string AlarmLevelWarning = "Alarm_Filter_Warning";

    public const string LangZhCn = "Language_zh_CN";
    public const string LangEnUs = "Language_en_US";
}
