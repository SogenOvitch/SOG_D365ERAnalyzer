namespace D365ERAnalyzer.Model;

/// <summary>
/// A pointer from a format component into a model mapping, produced by resolving a format binding.
/// <para>
/// The first three fields are the join key: a model mapping file holds several independent mapping
/// lines, and only the one whose model, version and root descriptor all match is the line the
/// format actually consumes. <see cref="ModelPath"/> is the remainder of the format expression
/// after its data source prefix, which is exactly how the binding paths are keyed.
/// </para>
/// </summary>
public sealed record ModelBindingReference(
    string? ModelGuid,
    string? ModelRevision,
    string? RootDescriptor,
    string ModelPath,
    string DatasourceName);
