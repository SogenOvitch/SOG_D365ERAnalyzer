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

    /// <summary>Mapping lines contributed by other files, with a note on where each came from.</summary>
    private IReadOnlyList<MappingSource> _external = Array.Empty<MappingSource>();

    public ModelMappingPaneViewModel() : base(ErConfigKind.ModelMapping) { }

    public override string OptionLabel => "Mapping line";

    /// <summary>The mapping line currently on screen.</summary>
    public ErMappingDefinition? CurrentMapping => SelectedOption?.Payload as ErMappingDefinition;

    /// <summary>
    /// Lists this file's mapping lines and any embedded in the other loaded files.
    /// <para>
    /// Mapping lines are not confined to model mapping files: a format can carry its own, and a
    /// model is expected to be able to as well. They belong in the same list because the reader is
    /// asking the same question of all of them, but each carries a note saying where it came from
    /// so a line living inside a format is never mistaken for one of this file's own.
    /// </para>
    /// </summary>
    protected override IEnumerable<PaneOption> BuildOptions(ErConfiguration? configuration)
    {
        foreach (var mapping in configuration?.ModelMapping?.Mappings ?? Enumerable.Empty<ErMappingDefinition>())
            yield return OptionFor(mapping, origin: null);

        foreach (var source in _external)
            yield return OptionFor(source.Mapping, source.Origin);
    }

    private static PaneOption OptionFor(ErMappingDefinition mapping, string? origin) => new()
    {
        Display = mapping.Name,
        Detail  = TextUtil.Join(mapping.RootDescriptor,
                                mapping.ModelVersion is null ? null : $"v{mapping.ModelVersion}",
                                mapping.IsImport ? "import" : null,
                                origin),
        Payload = mapping
    };

    /// <summary>Replaces the lines contributed by other files and rebuilds the selector.</summary>
    public void SetExternalMappings(IReadOnlyList<MappingSource> mappings)
    {
        _external = mappings;
        RefreshOptions();

        // The status was written while this file loaded, before any external line had arrived, so
        // it would otherwise keep reporting a count that no longer matches the selector.
        if (Configuration is not null || mappings.Count > 0)
            Status = DescribeContent(Configuration);
    }

    protected override string DescribeContent(ErConfiguration? configuration)
    {
        var own = configuration?.ModelMapping?.Mappings ?? (IReadOnlyList<ErMappingDefinition>)Array.Empty<ErMappingDefinition>();
        if (own.Count == 0 && _external.Count == 0) return "No mapping.";

        var bindings = own.Concat(_external.Select(e => e.Mapping)).Sum(m => m.Bindings.Count);
        var borrowed = _external.Count == 0 ? "" : $" (+{_external.Count} from other files)";

        return $"{own.Count + _external.Count} mapping lines{borrowed}  ·  {bindings} bindings total";
    }

    protected override IEnumerable<TreeSectionViewModel> BuildSections(
        ErConfiguration? configuration, PaneOption? option)
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
                         mapping.ModelVersion is null ? null : $"model version: {mapping.ModelVersion}",
                         mapping.DirectionName),
            Path   = mapping.Name
        };
        foreach (var source in TreeSort.Sorted(mapping.Datasources, d => d.Name))
            sourceRoot.Children.Add(DatasourceNode(source, _sourcesByPath, Labels));
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
        Dictionary<string, TreeNodeViewModel>? index = null,
        LabelContext? labels = null)
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
            Tooltip    = TextUtil.Join(source.FullPath,
                                       labels?.Display(source.Help) ?? source.Help,
                                       source.Expression)
        };

        index?.TryAdd(source.FullPath, node);

        // A group-by carries its grouped fields and aggregations in wrapper elements rather than
        // as data source children, so they would otherwise be invisible in the tree.
        if (source.GroupBy is { } groupBy)
            foreach (var child in GroupByNodes(groupBy, source.FullPath, index))
                node.Children.Add(child);

        foreach (var child in TreeSort.Sorted(source.Children, c => c.Name))
            node.Children.Add(DatasourceNode(child, index, labels));

        return node;
    }

    /// <summary>
    /// The grouped fields and aggregations of a group-by, as rows that take part in reference
    /// resolution like any other.
    /// <para>
    /// Both kinds read the field they are computed from, so they carry it as a referenced path.
    /// Aggregations are also read <i>by</i> others, and the address used for that is not the field
    /// they aggregate but "&lt;the group-by&gt;/aggregated/&lt;name&gt;" — 130 paths across the
    /// samples are written that way — so they are indexed under it. An aggregation with no name of
    /// its own is addressed by the last segment of its field path.
    /// </para>
    /// </summary>
    private static IEnumerable<TreeNodeViewModel> GroupByNodes(
        ErGroupBySpec groupBy, string parentPath, Dictionary<string, TreeNodeViewModel>? index)
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

            foreach (var field in groupBy.GroupedFields.OrderBy(f => f, TreeSort.ByName))
                grouped.Children.Add(new TreeNodeViewModel
                {
                    Header          = LastSegment(field),
                    Badge           = "group by",
                    Detail          = field,
                    Path            = field,
                    ReferencedPaths = new[] { field },
                    Tooltip         = field
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

        foreach (var aggregation in TreeSort.Sorted(groupBy.Aggregations,
                                                    a => a.Name ?? LastSegment(a.FieldPath)))
        {
            var kind = aggregation.Kind == ErAggregationKind.Unknown && aggregation.RawKind != 0
                ? $"agg {aggregation.RawKind}"
                : aggregation.Kind.ToDisplayName();

            // Unnamed aggregations are shown, and addressed, by the field they aggregate.
            var name = aggregation.Name ?? LastSegment(aggregation.FieldPath);
            var address = $"{parentPath}/aggregated/{name}";

            var node = new TreeNodeViewModel
            {
                Header          = name,
                Badge           = kind,
                Detail          = aggregation.FieldPath,
                Path            = address,
                ReferencedPaths = new[] { aggregation.FieldPath },
                Tooltip         = TextUtil.Join(address, aggregation.FieldPath)
            };

            index?.TryAdd(address, node);
            aggregations.Children.Add(node);
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

        public SortedDictionary<string, Trie> Children { get; } = new(TreeSort.ByName);
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
        // Not "does this pane own a file": a line can come from a model or a format, and those
        // are just as navigable.
        if (Options.Count == 0)
            return "No mapping lines available.";

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

        var best = Options
            .Select(o => new { Option = o, Mapping = o.Payload as ErMappingDefinition })
            .Where(x => x.Mapping is not null && MatchesLine(x.Mapping, reference))
            .OrderByDescending(x => DeclaresRoot(x.Mapping!, reference) ? 1 : 0)
            .ThenByDescending(x => x.Mapping!.ModelVersion == revision ? 1 : 0)
            .ThenByDescending(x => x.Mapping!.IsImport ? 0 : 1)
            .FirstOrDefault();

        // A line naming no root descriptor agrees on the model alone, which is far too weak to call
        // "the line this format uses" — better to say nothing than to point somewhere wrong.
        if (best is null || !DeclaresRoot(best.Mapping!, reference)) return null;

        if (!ReferenceEquals(best.Option, SelectedOption)) SelectedOption = best.Option;
        return best.Option.Display;
    }

    /// <summary>Explains why no line could be chosen, for the status bar.</summary>
    public string DescribeMissingLine(string? modelGuid, string? revision, string? rootDescriptor)
    {
        var lines = Options.Select(o => o.Payload).OfType<ErMappingDefinition>().ToList();
        if (lines.Count == 0) return "the file holds no mapping lines";

        var sameModel = lines.Count(m =>
            Guid.TryParse(m.ModelGuid, out var a) && Guid.TryParse(modelGuid, out var b) && a == b);

        if (sameModel == 0) return "no line targets that model";

        var versions = string.Join("/", lines.Select(m => m.ModelVersion).Distinct());
        var rootless = lines.Count(m => m.RootDescriptor is null);

        return rootless == lines.Count
            ? $"{sameModel} line(s) target the model but none declares a root descriptor; " +
              $"the format wants {rootDescriptor} v{revision}, the lines are v{versions}"
            : $"no line declares root {rootDescriptor}; the lines are v{versions}";
    }

    /// <summary>
    /// Whether a mapping line can serve a reference.
    /// <para>
    /// The model GUID must agree, and the root descriptor too when the line declares one. The
    /// version deliberately need not: a format built against model v40 runs against a v76 mapping,
    /// and demanding equality would reject every pairing that has since moved on. Version agreement
    /// is used to rank candidates instead.
    /// </para>
    /// </summary>
    private static bool MatchesLine(ErMappingDefinition mapping, ModelBindingReference reference)
    {
        // GUID casing differs between the format and the mapping, so compare parsed values.
        if (Guid.TryParse(mapping.ModelGuid, out var a) && Guid.TryParse(reference.ModelGuid, out var b) && a != b)
            return false;

        if (reference.RootDescriptor is null || mapping.RootDescriptor is null)
            return true;

        return string.Equals(mapping.RootDescriptor, reference.RootDescriptor, StringComparison.OrdinalIgnoreCase);
    }

    private static bool DeclaresRoot(ErMappingDefinition mapping, ModelBindingReference reference) =>
        reference.RootDescriptor is not null && mapping.RootDescriptor is not null;

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

    /// <summary>
    /// Names of the current line's data sources that are backed by a format rather than the model.
    /// Paths beneath these name format components, so they are what makes a mapping embedded in a
    /// format resolvable against the format pane.
    /// </summary>
    public IReadOnlyCollection<string> FormatDatasourceNames =>
        CurrentMapping?.Datasources
            .Where(d => d.IsFormatSource)
            .Select(d => d.Name)
            .ToList()
        ?? (IReadOnlyCollection<string>)Array.Empty<string>();

    /// <summary>Every row of this pane, both sections, for a reverse scan.</summary>
    public IEnumerable<TreeNodeViewModel> AllRows()
    {
        foreach (var section in new[] { PrimarySection, SecondarySection })
        foreach (var root in section?.Nodes ?? Enumerable.Empty<TreeNodeViewModel>())
        foreach (var row in root.DescendantsAndSelf())
            yield return row;
    }
}
