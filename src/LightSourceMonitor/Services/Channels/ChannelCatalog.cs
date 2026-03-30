using LightSourceMonitor.Models;
using Microsoft.Extensions.Options;

namespace LightSourceMonitor.Services.Channels;

public interface IChannelCatalog
{
    IReadOnlyList<LaserChannel> GetAllChannels();
    IReadOnlyList<LaserChannel> GetEnabledChannels();
    LaserChannel? GetById(int channelId);
    IReadOnlyDictionary<int, LaserChannel> GetChannelMap();
}

public class ChannelCatalog : IChannelCatalog
{
    private readonly IReadOnlyList<LaserChannel> _allChannels;
    private readonly IReadOnlyDictionary<int, LaserChannel> _channelMap;

    public ChannelCatalog(IOptions<DriverSettings> driverOptions)
    {
        ArgumentNullException.ThrowIfNull(driverOptions);

        var channels = new List<LaserChannel>();
        var usedIds = new HashSet<int>();

        foreach (var device in driverOptions.Value.GetEffectiveDevices().Where(d => d.Enabled))
        {
            foreach (var channel in device.Channels)
            {
                var id = CreateStableId(device.DeviceSN, channel.ChannelIndex, usedIds);
                channels.Add(new LaserChannel
                {
                    Id = id,
                    DeviceSN = device.DeviceSN,
                    ChannelIndex = channel.ChannelIndex,
                    ChannelName = string.IsNullOrWhiteSpace(channel.ChannelName) ? $"CH{channel.ChannelIndex + 1}" : channel.ChannelName,
                    SpecWavelength = channel.SpecWavelength,
                    SpecPowerMin = channel.SpecPowerMin,
                    SpecPowerMax = channel.SpecPowerMax,
                    AlarmDelta = channel.AlarmDelta,
                    IsEnabled = channel.IsEnabled
                });
            }
        }

        _allChannels = channels
            .OrderBy(c => c.DeviceSN, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.ChannelIndex)
            .ToList();
        _channelMap = _allChannels.ToDictionary(c => c.Id);
    }

    public IReadOnlyList<LaserChannel> GetAllChannels() => _allChannels;

    public IReadOnlyList<LaserChannel> GetEnabledChannels() => _allChannels.Where(c => c.IsEnabled).ToList();

    public LaserChannel? GetById(int channelId) => _channelMap.TryGetValue(channelId, out var channel) ? channel : null;

    public IReadOnlyDictionary<int, LaserChannel> GetChannelMap() => _channelMap;

    private static int CreateStableId(string deviceSn, int channelIndex, HashSet<int> usedIds)
    {
        var seed = $"{deviceSn}:{channelIndex}";
        uint hash = 2166136261;

        foreach (var ch in seed)
        {
            hash ^= ch;
            hash *= 16777619;
        }

        var candidate = (int)(hash & 0x7FFFFFFF);
        if (candidate == 0)
            candidate = 1;

        while (!usedIds.Add(candidate))
        {
            candidate++;
            if (candidate == int.MaxValue)
                candidate = 1;
        }

        return candidate;
    }
}