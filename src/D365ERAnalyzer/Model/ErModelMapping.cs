using System.Collections.Generic;

namespace D365ERAnalyzer.Model;

/// <summary>A model mapping configuration: one or more independent mapping lines.</summary>
public sealed class ErModelMappingSet
{
    public List<ErMappingDefinition> Mappings { get; } = new();
}

/// <summary>
/// One "model to datasource mapping" line. The triple
/// (<see cref="ModelGuid"/>, <see cref="ModelVersion"/>, <see cref="RootDescriptor"/>) is what a
/// format's data source matches against to pick this line out of the several in a file.
/// </summary>
public sealed class ErMappingDefinition
{
    public string? Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? BaseReference { get; init; }

    public string? ModelGuid { get; init; }
    public string? ModelName { get; init; }

    /// <summary>Version number pulled off <c>@ModelVersion</c> ("{guid},7" → "7").</summary>
    public string? ModelVersion { get; init; }

    /// <summary>The model root descriptor this line maps (<c>@DataContainerDescriptor</c>).</summary>
    public string? RootDescriptor { get; init; }

    public List<ErDatasourceNode> Datasources { get; } = new();
    public List<ErModelBinding> Bindings { get; } = new();

    public Dictionary<string, ErModelBinding> BindingsByPath { get; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>A model field bound to a data source expression (<c>ERDataContainerPathBinding</c>).</summary>
public sealed class ErModelBinding
{
    /// <summary>Model path relative to the mapping's root descriptor, e.g. "InvoiceBase/Id".</summary>
    public required string Path { get; init; }

    /// <summary>The formula as the designer shows it. Display only — never split this on '.'.</summary>
    public string? Expression { get; init; }

    /// <summary>Data source paths swept out of the expression AST.</summary>
    public List<string> ReferencedPaths { get; } = new();
}
