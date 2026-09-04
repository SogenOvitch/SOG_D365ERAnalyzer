using D365ERAnalyzer.Model;

namespace D365ERAnalyzer.ViewModels;

/// <summary>
/// A mapping line contributed by a file other than the model mapping, together with a note saying
/// where it came from so it can be told apart in the selector.
/// </summary>
public sealed record MappingSource(ErMappingDefinition Mapping, string Origin);
