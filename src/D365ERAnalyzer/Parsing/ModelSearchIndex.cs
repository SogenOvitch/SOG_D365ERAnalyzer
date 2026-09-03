using D365ERAnalyzer.Model;

namespace D365ERAnalyzer.Parsing;

/// <summary>
/// Makes a data model searchable without materialising its tree.
/// <para>
/// The model is a flat graph of 153 descriptors, but the tree it produces runs to tens of
/// thousands of nodes and is built lazily, so a walk over the visible tree can only find what the
/// user has already opened. Indexing the <i>graph</i> instead costs one breadth-first pass and
/// about 1 500 entries, and yields a path the pane can then open on demand.
/// </para>
/// <para>
/// Breadth-first is deliberate: each descriptor is reached by its shortest path from a root, so a
/// hit is revealed at the shallowest place it occurs rather than an arbitrary deep one. A
/// descriptor reachable from several roots is indexed once.
/// </para>
/// </summary>
public sealed class ModelSearchIndex
{
    public sealed record Entry(string Name, string Path, string Kind);

    private readonly List<Entry> _entries;

    private ModelSearchIndex(List<Entry> entries) => _entries = entries;

    public int Count => _entries.Count;

    public static ModelSearchIndex Build(ErDataModel model)
    {
        var entries = new List<Entry>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(ErDescriptor Descriptor, string Path)>();

        foreach (var root in model.Roots)
        {
            if (!visited.Add(root.Name)) continue;

            entries.Add(new Entry(root.Name, root.Name, root.IsEnum ? "enum" : "record"));
            queue.Enqueue((root, root.Name));
        }

        while (queue.Count > 0)
        {
            var (descriptor, path) = queue.Dequeue();

            foreach (var item in descriptor.Items)
            {
                var itemPath = $"{path}/{item.Name}";
                entries.Add(new Entry(item.Name, itemPath, item.Type.ToDisplayName()));

                var target = model.Find(item.TypeDescriptor);
                if (target is null || !visited.Add(target.Name)) continue;

                queue.Enqueue((target, itemPath));
            }
        }

        return new ModelSearchIndex(entries);
    }

    /// <summary>
    /// Paths of entries matching <paramref name="term"/>, shallowest first so the most useful
    /// hits survive the limit.
    /// </summary>
    public IReadOnlyList<string> FindPaths(string term, bool exactMatch, int limit, out int total)
    {
        var matches = _entries.Where(e => Matches(e, term, exactMatch)).ToList();
        total = matches.Count;

        return matches
            .OrderBy(e => e.Path.Count(c => c == '/'))
            .ThenBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(e => e.Path)
            .ToList();
    }

    private static bool Matches(Entry entry, string term, bool exactMatch)
    {
        if (exactMatch)
            return entry.Name.Equals(term, StringComparison.OrdinalIgnoreCase)
                || entry.Path.Equals(term, StringComparison.OrdinalIgnoreCase);

        return entry.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
            || entry.Path.Contains(term, StringComparison.OrdinalIgnoreCase);
    }
}
