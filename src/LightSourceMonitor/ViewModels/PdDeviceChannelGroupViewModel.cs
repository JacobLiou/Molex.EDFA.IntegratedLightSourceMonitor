using System.Collections.ObjectModel;
using LightSourceMonitor.Models;

namespace LightSourceMonitor.ViewModels;

/// <summary>设置页「PD 通道配置」中按设备分组的 Tab 项：Header 为设备标识，内容为该设备下的通道列表。</summary>
public sealed class PdDeviceChannelGroupViewModel
{
    public string DeviceSn { get; init; } = "";

    /// <summary>Tab 标题：有序号 + 序列号（或「设备 n」当 SN 为空）。</summary>
    public string TabHeader { get; init; } = "";

    public int DeviceOrdinal { get; init; }

    public ObservableCollection<LaserChannel> Channels { get; } = new();

    public string DisplaySn => string.IsNullOrWhiteSpace(DeviceSn) ? "—" : DeviceSn;
}
