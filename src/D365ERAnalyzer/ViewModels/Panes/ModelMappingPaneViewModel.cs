using D365ERAnalyzer.Model;

namespace D365ERAnalyzer.ViewModels.Panes;

/// <summary>
/// Renders one mapping line at a time, split into two independently scrollable sections: its data
/// sources above, its bindings below.
/// <para>
/// A model mapping file holds several independent lines — the sample has seven — and only the one
/// matching a format data source is relevant, so the selector stays.
/// </para>
/// </summary>
public sealed class ModelMappingPaneViewModel : ConfigPaneViewModel
{
    /// <summary>Data source rows by full path, for resolving what a selected binding refers to.</summary>
    private readonly Dictionary<string, TreeNodeViewModel> _sourcesByPath =
        new(StringComparer.OrdinalIgnoreCase);

    private TreeNodeViewModel? _bindingRoot;

    public ModelMappingPaneViewModel() : base(ErConfigKind.ModelMapping) { }

    public override string OptionLabel => "Mapping line";

    /// <summary>The mapping line currently on screen.</summary>
    public ErMappingDefinition? CurrentMapping => SelectedOption?.Payload as ErMappingDefinition;

    protected override IEnumerable<PaneOption> BuildOptions(ErConfiguration configuration)
    {
        if (configuration.ModelMapping is not { } set) yield break;

        foreach (var mapping in set.Mappings)
            yield return new PaneOption
            {
                Display = mapping.Name,
                Detail  = TextUtil.Join(mapping.RootDescriptor,
                                        mapping.ModelVersion is null ? null : $"v{mapping.ModelVersion}"),
                Payload = mapping
            };
    }

    protected override string DescribeContent(ErConfiguration configuration)
    {
        var set = configuration.ModelMapping;
        if (set is null) return "No mapping.";

        var bindings = set.Mappings.Sum(m => m.Bindings.Count);
        return $"{set.Mappings.Count} mapping lines  ·  {bindings} bindings total";
    }

    protected override IEnumerable<TreeSectionViewModel> BuildSections(
        ErConfiguration configuration, PaneOption? option)
    {
        _sourcesByPath.Clear();
        _bindingRoot = null;

        var sources  = Section("Data sources", PaneMarker.DataSources);
        var bindings = Section("Bindings", PaneMarker.Bindings);

        if (option?.Payload is not ErMappingDefinition mapping)
            return new[] { sources, bindings };

        var sourceRoot = new TreeNodeViewModel
        {
            Header = "Data sources",
            Badge  = CountLabel(mapping.Datasources.Sum(Count)),
            // The triple a format matches on to pick this line out of the several in the file.
            Detail = TextUtil.Join(
                         mapping.RootDescriptor is null ? null : $"root: {mapping.RootDescriptor}",
                         mapping.ModelVersion is null ? null : $"model version: {mapping.ModelVersion}"),
            Path   = mapping.Name
        };
        foreach (var source in mapping.Datasources)
            sourceRoot.Children.Add(DatasourceNode(source, _sourcesByPath));
        sourceRoot.IsExpanded = true;
        sources.Nodes.Add(sourceRoot);

        _bindingRoot = BuildBindingTree(mapping);
        bindings.Nodes.Add(_bindingRoot);

        return new[] { sources, bindings };
    }

    // ------------------------------------------------------------- data sources

    /// <summary>Shared with the format pane — both mappings serialize data sources identically.</summary>
    internal static TreeNodeViewModel DatasourceNode(
        ErDatasourceNode source,
        Dictionary<string, TreeNodeViewModel>? index = null)
    {
        var node = new TreeNodeViewModel
        {
            Header     = source.Name,
            Badge      = source.SourceKind,
            Detail     = TextUtil.Join(source.SourceDetail, TextUtil.OneLine(source.Expression)),
            Path            = source.FullPath,
            Expression      = source.Expression,
            Payload         = source,
            ReferencedPaths = source.ReferencedPaths,
            Tooltip    = TextUtil.Join(source.FullPath, source.Help, source.Expression)
        };

        index?.TryAdd(source.FullPath, node);

        // A group-by carries its grouped fields and aggregations in wrapper elements rather than
        // as data source children, so they would otherwise be invisible in the tree.
        if (source.GroupBy is { } groupBy)
            foreach (var child in GroupByNodes(groupBy, source.FullPath))
                node.Children.Add(child);

        foreach (var child in source.Children)
            node.Children.Add(DatasourceNode(child, index));

        return node;
    }

    private static IEnumerable<TreeNodeViewModel> GroupByNodes(ErGroupBySpec groupBy, string parentPath)
    {
        if (groupBy.GroupedFields.Count > 0)
        {
            var grouped = new TreeNodeViewModel
            {
                Header = "Grouped fields",
                Badge  = $"{groupBy.GroupedFields.Count}",
                Path   = $"{parentPath}/GroupedFields",
                IsExpanded = true
            };

            foreach (var field in groupBy.GroupedFields)
                grouped.Children.Add(new TreeNodeViewModel
                {
                    Header = LastSegment(field),
                    Badge  = "group by",
                    Detail = field,
                    Path   = field
                });

            yield return grouped;
        }

        if (groupBy.Aggregations.Count == 0) yield break;

        var aggregations = new TreeNodeViewModel
        {
            Header = "Aggregations",
            Badge  = $"{groupBy.Aggregations.Count}",
            Path   = $"{parentPath}/Aggregations",
            IsExpanded = true
        };

        foreach (var aggregation in groupBy.Aggregations)
        {
            var kind = aggregation.Kind == ErAggregationKind.Unknown && aggregation.RawKind != 0
                ? $"agg {aggregation.RawKind}"
                : aggregation.Kind.ToDisplayName();

            aggregations.Children.Add(new TreeNodeViewModel
            {
                // Unnamed aggregations are shown by the field they aggregate.
                Header = aggregation.Name ?? LastSegment(aggregation.FieldPath),
                Badge  = kind,
                Detail = aggregation.FieldPath,
                Path   = aggregation.FieldPath
            });
        }

        yield return aggregations;
    }

    private static string LastSegment(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash >= 0 && slash < path.Length - 1 ? path[(slash + 1)..] : path;
    }

    // ----------------------------------------------------------------- bindings

    /// <summary>
    /// Nests the bindings by their path segments, so "Customer/BankAccount/AccountNum" reads as
    /// Customer → BankAccount → AccountNum rather than one flat row. An intermediate segment can
    /// carry a binding of its own — a list is bound, and so are the fields inside it — so a node
    /// is both a parent and a binding whenever the paths say so.
    /// </summary>
    private static TreeNodeViewModel BuildBindingTree(ErMappingDefinition mapping)
    {
        var root = new Trie("", "");

        foreach (var binding in mapping.Bindings)
        {
            var current = root;
            var built = "";

            foreach (var segment in binding.Path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                built = built.Length == 0 ? segment : $"{built}/{segment}";

                if (!current.Children.TryGetValue(segment, out var next))
                    current.Children[segment] = next = new Trie(segment, built);

                current = next;
            }

            current.Binding = binding;
        }

        var node = new TreeNodeViewModel
        {
            Header = "Bindings",
            Badge  = CountLabel(mapping.Bindings.Count),
            Path   = mapping.Name,
            IsExpanded = true
        };

        foreach (var child in root.Children.Values)
            node.Children.Add(ToNode(child));

        return node;
    }

    private static TreeNodeViewModel ToNode(Trie trie)
    {
        var binding = trie.Binding;

        var node = new TreeNodeViewModel
        {
            Header          = trie.Name,
            Badge           = binding is null ? null : "binding",
            Detail          = TextUtil.OneLine(binding?.Expression),
            Path            = trie.Path,
            Expression      = binding?.Expression,
            Payload         = binding,
            ReferencedPaths = binding?.ReferencedPaths ?? (IReadOnlyList<string>)Array.Empty<string>(),
            Tooltip         = binding?.Expression
        };

        foreach (var child in trie.Children.Values)
            node.Children.Add(ToNode(child));

        return node;
    }

    private sealed class Trie(string name, string path)
    {
        public string Name { get; } = name;
        public string Path { get; } = path;
        public ErModelBinding? Binding { get; set; }

        public SortedDictionary<string, Trie> Children { get; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- lookups

    /// <summary>
    /// The data source rows a set of referenced paths lands in. Paths reach into table fields —
    /// "CustInvoiceJour/InvoiceId" — but only the data source itself is a row, so the longest
    /// declared prefix is the row worth marking.
    /// </summary>
    public IReadOnlyList<TreeNodeViewModel> FindSourceRows(IEnumerable<string> paths)
    {
        var found = new List<TreeNodeViewModel>();

        foreach (var path in paths)
        {
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

            for (var length = segments.Length; length > 0; length--)
            {
                var candidate = string.Join('/', segments.Take(length));
                if (!_sourcesByPath.TryGetValue(candidate, out var node)) continue;

                if (!found.Contains(node)) found.Add(node);
                break;
            }
        }

        return found;
    }

    /// <summary>
    /// The binding row for a model path, but only when the line on screen is the one the caller
    /// means. A format row points at a specific mapping line, and marking a same-named path in a
    /// different line would be a lie.
    /// </summary>
    public TreeNodeViewModel? FindBindingRow(string? rootDescriptor, string modelPath)
    {
        if (rootDescriptor is not null &&
            !string.Equals(CurrentMapping?.RootDescriptor, rootDescriptor, StringComparison.OrdinalIgnoreCase))
            return null;

        return FindBindingNode(modelPath, out _);
    }

    // ------------------------------------------------- jump here from the format pane

    /// <summary>
    /// Selects the mapping line the reference points at and, within it, the bound row. Selecting
    /// the row highlights the data sources its formula uses, which is the point of the jump.
    /// </summary>
    /// <returns>What happened, for the status line.</returns>
    public string LocateBinding(ModelBindingReference reference)
    {
        if (Configuration?.ModelMapping is null)
            return "No model mapping loaded.";

        var option = Options.FirstOrDefault(o => o.Payload is ErMappingDefinition m && MatchesLine(m, reference));
        if (option is null)
            return $"No mapping line for root “{reference.RootDescriptor}” at model version {reference.ModelRevision}.";

        var mappingName = option.Display;

        // Changing the selection rebuilds both sections, so this must happen before the lookup.
        if (!ReferenceEquals(option, SelectedOption))
            SelectedOption = option;

        var node = FindBindingNode(reference.ModelPath, out var exact);
        if (node is null)
            return $"“{mappingName}” has no binding under {reference.ModelPath}.";

        RevealBinding(node);
        SecondarySection?.SelectAndReveal(node);

        var source = $"{reference.DatasourceName}.{reference.ModelPath.Replace('/', '.')}";

        return exact
            ? $"{source} → “{mappingName}” · {node.Path}"
            : $"{source} → “{mappingName}” · nearest bound row {node.Path} (no binding on the full path)";
    }

    /// <summary>
    /// Selects the mapping line matching a model GUID, revision and root descriptor.
    /// </summary>
    /// <returns>The line name, or null when none matches.</returns>
    public string? SelectMappingLine(string? modelGuid, string? revision, string? rootDescriptor)
    {
        var reference = new ModelBindingReference(modelGuid, revision, rootDescriptor, "", "");

        var option = Options.FirstOrDefault(
            o => o.Payload is ErMappingDefinition m && MatchesLine(m, reference));

        if (option is null) return null;

        if (!ReferenceEquals(option, SelectedOption)) SelectedOption = option;
        return option.Display;
    }

    private static bool MatchesLine(ErMappingDefinition mapping, ModelBindingReference reference)
    {
        // GUID casing differs between the format and the mapping, so compare parsed values.
        if (Guid.TryParse(mapping.ModelGuid, out var a) && Guid.TryParse(reference.ModelGuid, out var b) && a != b)
            return false;

        if (reference.ModelRevision is not null && mapping.ModelVersion is not null &&
            !mapping.ModelVersion.Equals(reference.ModelRevision, StringComparison.OrdinalIgnoreCase))
            return false;

        return reference.RootDescriptor is null ||
               string.Equals(mapping.RootDescriptor, reference.RootDescriptor, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Walks the binding tree along a model path. Not every path is bound at its full depth — a
    /// list can be bound while its fields are bound individually, or the other way round — so the
    /// deepest row actually reached is returned, and <paramref name="exact"/> says whether that was
    /// the whole path.
    /// </summary>
    private TreeNodeViewModel? FindBindingNode(string path, out bool exact)
    {
        exact = false;

        var current = _bindingRoot;
        if (current is null) return null;

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        TreeNodeViewModel? deepest = null;
        var matched = 0;

        foreach (var segment in segments)
        {
            var next = current.Children.FirstOrDefault(
                c => c.Header.Equals(segment, StringComparison.OrdinalIgnoreCase));

            if (next is null) break;

            current = next;
            deepest = next;
            matched++;
        }

        exact = matched == segments.Length;
        return deepest;
    }

    private void RevealBinding(TreeNodeViewModel target)
    {
        if (_bindingRoot is not null) ExpandTowards(_bindingRoot, target);
    }

    private static bool ExpandTowards(TreeNodeViewModel current, TreeNodeViewModel target)
    {
        if (ReferenceEquals(current, target)) return true;

        foreach (var child in current.Children)
        {
            if (!ExpandTowards(child, target)) continue;

            current.IsExpanded = true;
            return true;
        }

        return false;
    }

    private static int Count(ErDatasourceNode node) => 1 + node.Children.Sum(Count);

    private static string CountLabel(int count) => count == 1 ? "1 item" : $"{count} items";

    /// <summary>Selects the binding row at a model path, if this mapping line has one.</summary>
    public bool SelectBindingPath(string path)
    {
        var node = FindBindingNode(path, out _);
        if (node is null) return false;

        RevealBinding(node);
        SecondarySection?.SelectAndReveal(node);
        return true;
    }

    /// <summary>
    /// Rows in this pane whose formula reads <paramref name="path"/> or something under it — other
    /// data sources as well as bindings, since a calculated field can be built from another.
    /// </summary>
    public IEnumerable<TreeNodeViewModel> FindRowsReferencing(string path)
    {
        foreach (var section in new[] { PrimarySection, SecondarySection })
        foreach (var root in section?.Nodes ?? Enumerable.Empty<TreeNodeViewModel>())
        foreach (var row in root.DescendantsAndSelf())
        {
            if (row.Path is not null && row.Path.Equals(path, StringComparison.OrdinalIgnoreCase))
                continue;   // the selected row itself

            if (Reads(row, path)) yield return row;
        }
    }

    internal static bool Reads(TreeNodeViewModel row, string path) =>
        row.ReferencedPaths.Any(p =>
            p.Equals(path, StringComparison.OrdinalIgnoreCase) ||
            p.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase));
}
