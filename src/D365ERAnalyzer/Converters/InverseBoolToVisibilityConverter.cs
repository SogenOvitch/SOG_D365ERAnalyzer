using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace D365ERAnalyzer.Converters;

/// <summary>Shows an element only when the bound flag is false.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
