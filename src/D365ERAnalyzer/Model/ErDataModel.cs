using System.Collections.Generic;

namespace D365ERAnalyzer.Model;

/// <summary>
/// A parsed data model. The descriptors are a <b>flat graph</b>, not a tree: the hierarchy is
/// produced by resolving <see cref="ErDescriptorItem.TypeDescriptor"/> against
/// <see cref="Descriptors"/>, starting from a root. The graph may contain cycles.
/// </summary>
public sealed class ErDataModel
{
    public string? Name { get; init; }
    public string? Id { get; init; }
    public string? Description { get; init; }

    /// <summary>The designer's last-selected root. Not the only root — see <see cref="Roots"/>.</summary>
    public string? DefaultRoot { get; init; }

    public Dictionary<string, ErDescriptor> Descriptors { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<ErDescriptor> Roots =>
        Descriptors.Values.Where(d => d.IsRoot).OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase);

    public ErDescriptor? Find(string? name) =>
        name is not null && Descriptors.TryGetValue(name, out var d) ? d : null;
}

public sealed class ErDescriptor
{
    public required string Name { get; init; }
    public string? Id { get; init; }
    public string? Label { get; init; }
    public string? Description { get; init; }
    public bool IsRoot { get; init; }
    public bool IsEnum { get; init; }

    public List<ErDescriptorItem> Items { get; } = new();
}

public sealed class ErDescriptorItem
{
    public required string Name { get; init; }
    public string? Label { get; init; }
    public string? Description { get; init; }

    public ErDataType Type { get; init; }

    /// <summary>Raw <c>@Type</c>, kept so an unrecognised value can still be shown.</summary>
    public int RawType { get; init; }

    /// <summary>Name of the descriptor this item expands into, when it has one.</summary>
    public string? TypeDescriptor { get; init; }
}
