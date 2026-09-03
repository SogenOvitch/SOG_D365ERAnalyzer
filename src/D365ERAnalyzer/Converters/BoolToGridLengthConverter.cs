using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace D365ERAnalyzer.Converters;

/// <summary>
/// Collapses a grid row to zero height when the bound flag is false. Used to hide the second tree
/// section and its splitter in panes that only have one.
/// </summary>
public sealed class BoolToGridLengthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true) return new GridLength(0);

        var spec = parameter as string;
        if (string.IsNullOrEmpty(spec) || spec == "star")
            return new GridLength(1, GridUnitType.Star);

        return double.TryParse(spec, NumberStyles.Float, CultureInfo.InvariantCulture, out var px)
             ? new GridLength(px)
             : new GridLength(1, GridUnitType.Star);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
