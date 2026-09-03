namespace D365ERAnalyzer.ViewModels;

/// <summary>
/// An entry in a pane's selector: which model root to show, or which mapping line. A configuration
/// holds several of each, and only one can be rendered at a time.
/// </summary>
public sealed class PaneOption
{
    public required string Display { get; init; }
    public string? Detail { get; init; }
    public required object Payload { get; init; }

    public override string ToString() =>
        Detail is null ? Display : $"{Display}  —  {Detail}";
}
