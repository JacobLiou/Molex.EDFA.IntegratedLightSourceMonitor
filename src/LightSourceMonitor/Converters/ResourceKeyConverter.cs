using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LightSourceMonitor.Converters;

/// <summary>将资源键（如 TimeRange_1H）转为当前语言下的字符串。</summary>
public sealed class ResourceKeyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || string.IsNullOrEmpty(key))
            return value ?? "";

        return Application.Current?.TryFindResource(key) as string ?? key;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
