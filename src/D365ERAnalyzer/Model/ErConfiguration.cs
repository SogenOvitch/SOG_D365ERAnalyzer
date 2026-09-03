namespace D365ERAnalyzer.Model;

/// <summary>A fully parsed configuration: the envelope plus whichever payload its type carries.</summary>
public sealed class ErConfiguration
{
    public required ErEnvelope Envelope { get; init; }

    public ErDataModel? DataModel { get; init; }
    public ErModelMappingSet? ModelMapping { get; init; }
    public ErFormat? Format { get; init; }
}
