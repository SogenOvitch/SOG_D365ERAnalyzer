namespace D365ERAnalyzer.Model;

/// <summary>A fully parsed configuration: the envelope plus whichever payload its type carries.</summary>
public sealed class ErConfiguration
{
    public required ErEnvelope Envelope { get; init; }

    /// <summary>Embedded label translations. Empty when the export carries none.</summary>
    public ErLabels Labels { get; init; } = new();

    public ErDataModel? DataModel { get; init; }
    public ErModelMappingSet? ModelMapping { get; init; }
    public ErFormat? Format { get; init; }
}
