using System.Collections.Generic;

namespace D365ERAnalyzer.Model;

/// <summary>
/// A node in a mapping's data source tree. Serialized flat as
/// <c>ERModelItemDefinition/@ParentPath</c> + <c>ERModelItemValueDefinition/@Name</c>; the tree is
/// rebuilt from those paths. Shared by model mappings and format mappings — both use the identical
/// three-element shape.
/// </summary>
public sealed class ErDatasourceNode
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public string? Label { get; init; }
    public string? Help { get; init; }

    /// <summary>Data source kind, e.g. "Table", "Calculated", "Model", "Group by".</summary>
    public string? SourceKind { get; init; }

    /// <summary>Short qualifier for the kind, e.g. the table name or model root descriptor.</summary>
    public string? SourceDetail { get; init; }

    /// <summary>Present on calculated fields and group-by sources.</summary>
    public string? Expression { get; init; }

    // ---- set only when the source is an ERModelDataSourceHandler ----
    // Kept structured rather than folded into SourceDetail, because these three are the join key
    // that picks the right mapping line out of the several in a model mapping file.

    public string? ModelGuid { get; init; }
    public string? ModelRevision { get; init; }
    public string? ModelDescriptor { get; init; }

    public bool IsModelSource => ModelDescriptor is not null;

    /// <summary>
    /// True for the placeholder rows invented to bridge a declared node to a parent that lives in
    /// the model rather than in this mapping. They are scaffolding, not declarations.
    /// </summary>
    public bool IsSynthetic { get; init; }

    /// <summary>Set when the source is an ERModelGroupByFunction.</summary>
    public ErGroupBySpec? GroupBy { get; init; }

    /// <summary>Model/datasource paths referenced by <see cref="Expression"/>.</summary>
    public List<string> ReferencedPaths { get; } = new();

    public List<ErDatasourceNode> Children { get; } = new();
}
