using System.Windows.Media;

// The Color property shadows the Color type inside this class, so the media type is aliased.
using MediaColor = System.Windows.Media.Color;

namespace D365ERAnalyzer.ViewModels;

/// <summary>
/// The identity of a tree section, shown as a coloured dot beside its title and repeated on any
/// row that the section's current selection refers to.
/// <para>
/// A dot answers "who points at this row", which a single highlight colour could not: a row can be
/// referenced from several sections at once, and the dots simply accumulate. Keying on the section
/// rather than the pane also means selecting in Format mapping no longer wipes the marks a Format
/// selection just made — they are different sources.
/// </para>
/// </summary>
public sealed class PaneMarker
{
    private PaneMarker(string key, string display, string colour)
    {
        Key = key;
        Display = display;

        var value = (MediaColor)ColorConverter.ConvertFromString(colour)!;
        Color = Frozen(value);
        DimColor = Frozen(Darken(value, 0.5));
    }

    private static SolidColorBrush Frozen(MediaColor color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static MediaColor Darken(MediaColor color, double factor) => MediaColor.FromRgb(
        (byte)(color.R * factor), (byte)(color.G * factor), (byte)(color.B * factor));

    public string Key { get; }

    /// <summary>Section name, used as the dot tooltip.</summary>
    public string Display { get; }

    public Brush Color { get; }

    /// <summary>Same hue, darkened — used for rows that read the selection rather than feed it.</summary>
    public Brush DimColor { get; }

    public static PaneMarker DataModel { get; }     = new("model",     "Data model",     "#FF4FA3FF");
    public static PaneMarker DataSources { get; }   = new("sources",   "Data sources",   "#FF4EC94E");
    public static PaneMarker Bindings { get; }      = new("bindings",  "Bindings",       "#FFFFB44E");
    public static PaneMarker Format { get; }        = new("format",    "Format",         "#FFB48CFF");
    public static PaneMarker FormatMapping { get; } = new("formatmap", "Format mapping", "#FFFF6FA5");
}
