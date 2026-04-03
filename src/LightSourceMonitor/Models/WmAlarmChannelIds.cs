namespace LightSourceMonitor.Models;

/// <summary>将 WM 波长服务表格告警写入 <see cref="AlarmEvent.ChannelId"/>，与 PD 通道 ID 区分。</summary>
public static class WmAlarmChannelIds
{
    public const int Base = -900_000;

    public static int Encode(int wmChannelIndex) => Base - wmChannelIndex;

    public static bool TryDecode(int channelId, out int wmChannelIndex)
    {
        if (channelId > Base || channelId < Base - 63)
        {
            wmChannelIndex = 0;
            return false;
        }

        wmChannelIndex = Base - channelId;
        return wmChannelIndex is >= 0 and < 64;
    }

    public static bool IsWavelengthServiceAlarm(int channelId) =>
        channelId <= Base && channelId >= Base - 63;
}
