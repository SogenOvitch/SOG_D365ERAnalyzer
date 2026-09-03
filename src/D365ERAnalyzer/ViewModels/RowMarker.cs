using System.Windows.Media;

namespace D365ERAnalyzer.ViewModels;

/// <summary>Which way the reference runs between a marked row and the current selection.</summary>
public enum MarkerDirection
{
    /// <summary>The selection reads this row — this row is upstream of it.</summary>
    ReadBySelection,

    /// <summary>This row reads the selection — this row is downstream of it.</summary>
    ReadsSelection
}

/// <summary>
/// One dot on a row: which section's selection put it there, and which way the reference runs.
/// <para>
/// Both directions share the section's colour so the source stays obvious at a glance, with the
/// downstream direction darkened. Two hues per section would make five sections into ten colours
/// to learn; one hue plus a shade keeps "who" and "which way" separable.
/// </para>
/// </summary>
public sealed class RowMarker
{
    public required PaneMarker Source { get; init; }
    public required MarkerDirection Direction { get; init; }

    public Brush Color =>
        Direction == MarkerDirection.ReadBySelection ? Source.Color : Source.DimColor;

    public string Display => Direction == MarkerDirection.ReadBySelection
        ? $"{Source.Display}: the selected row reads this"
        : $"{Source.Display}: this row reads the selected row";

    public bool Matches(PaneMarker source, MarkerDirection direction) =>
        ReferenceEquals(Source, source) && Direction == direction;
}
