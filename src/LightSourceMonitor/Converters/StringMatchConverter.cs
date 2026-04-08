using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LightSourceMonitor.Converters;

/// <summary>Two-way: selected string equals <paramref name="parameter"/>.</summary>
public sealed class StringMatchConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter != null)
            return parameter.ToString()!;
        return Binding.DoNothing;
    }
}
