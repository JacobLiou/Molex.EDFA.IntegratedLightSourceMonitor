using System.Globalization;
using LightSourceMonitor.Models;

namespace LightSourceMonitor.Helpers;

public static class WmSnapshotCsvCodec
{
    public static WavelengthMeterSnapshot ToEntity(WavelengthTableSnapshot snapshot)
    {
        var ordered = snapshot.Rows.OrderBy(r => r.ChannelIndex).ToList();
        var wlParts = ordered.Select(r =>
            r.IsValid ? r.WavelengthNm.ToString(CultureInfo.InvariantCulture) : "");
        var pParts = ordered.Select(r =>
            r.IsValid ? r.WmPowerDbm.ToString(CultureInfo.InvariantCulture) : "");

        return new WavelengthMeterSnapshot
        {
            Timestamp = snapshot.Timestamp,
            QueryDeviceId = snapshot.QueryDeviceId ?? "",
            OneTimeValues = string.Join(',', wlParts),
            PowerValues = string.Join(',', pParts)
        };
    }

    /// <summary>解析一路波长；空段或不可解析返回 null。</summary>
    public static IReadOnlyList<double?> ParseWavelengthSegments(string? csv)
    {
        if (string.IsNullOrEmpty(csv))
            return Array.Empty<double?>();

        var parts = csv.Split(',');
        var list = new double?[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            var t = parts[i].Trim();
            if (t.Length == 0)
            {
                list[i] = null;
                continue;
            }

            list[i] = double.TryParse(t, CultureInfo.InvariantCulture, out var v) ? v : null;
        }

        return list;
    }

    public static IReadOnlyList<double?> ParsePowerSegments(string? csv)
    {
        if (string.IsNullOrEmpty(csv))
            return Array.Empty<double?>();

        var parts = csv.Split(',');
        var list = new double?[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            var t = parts[i].Trim();
            if (t.Length == 0)
            {
                list[i] = null;
                continue;
            }

            list[i] = double.TryParse(t, CultureInfo.InvariantCulture, out var v) ? v : null;
        }

        return list;
    }
}
