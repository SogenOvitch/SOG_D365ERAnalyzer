using System.Globalization;
using System.Windows.Data;

namespace D365ERAnalyzer.Converters;

/// <summary>
/// Multiplies a number by the converter parameter. Used to keep the smaller text in the trees
/// proportional to the window font size rather than pinned to a fixed value, so the font
/// multiplier scales everything together.
/// </summary>
public sealed class ScaleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var size = value as double? ?? 12d;
        var factor = double.TryParse(parameter as string, NumberStyles.Float,
                                     CultureInfo.InvariantCulture, out var f) ? f : 1d;

        return size * factor;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
