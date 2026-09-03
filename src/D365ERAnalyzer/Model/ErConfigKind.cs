namespace D365ERAnalyzer.Model;

/// <summary>
/// The three configuration types the analyzer understands. Determined by which versioned
/// object appears under the root <c>Contents.</c> element — see docs/er-xml-schema.md §1.
/// </summary>
public enum ErConfigKind
{
    Unknown,
    DataModel,
    ModelMapping,
    Format
}

public static class ErConfigKindExtensions
{
    public static string ToDisplayName(this ErConfigKind kind) => kind switch
    {
        ErConfigKind.DataModel    => "Data model",
        ErConfigKind.ModelMapping => "Model mapping",
        ErConfigKind.Format       => "Format",
        _                         => "Unknown"
    };
}
