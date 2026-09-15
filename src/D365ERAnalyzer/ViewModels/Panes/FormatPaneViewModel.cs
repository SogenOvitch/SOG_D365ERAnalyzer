using D365ERAnalyzer.Model;

namespace D365ERAnalyzer.ViewModels.Panes;

/// <summary>
/// Renders a format configuration in two independently scrollable sections: the component tree
/// above, the format mapping that binds it below. They are separate versioned objects in one file.
/// <para>
/// The component tree carries no bindings of its own — they are joined in by component GUID from
/// the format mapping, which is the first half of the resolution chain.
/// </para>
/// </summary>
public sealed class FormatPaneViewModel : ConfigPaneViewModel
{
    /// <summary>Direct references per row, resolved once when the tree is built. Drives the dots.</summary>
    private readonly Dictionary<TreeNodeViewModel, IReadOnlyList<ModelBindingReference>> _references = new();

    /// <summary>
    /// References found by following calculated fields onwards. Only the trace menu uses these:
    /// a jump is a deliberate question about where a value comes from, and following the chain is
    /// the answer, whereas the dots stay literal so they can be trusted at a glance.
    /// </summary>
    private readonly Dictionary<TreeNodeViewModel, IReadOnlyList<ModelBindingReference>> _deepReferences = new();

    /// <summary>Format mapping data source rows by full path, for the green highlight.</summary>
    private readonly Dictionary<string, TreeNodeViewModel> _sourcesByPath =
        new(StringComparer.OrdinalIgnoreCase);

    public FormatPaneViewModel() : base(ErConfigKind.Format) { }

    public override string OptionLabel => "";

    protected override IEnumerable<PaneOption> BuildOptions(ErConfiguration? configuration) =>
        Enumerable.Empty<PaneOption>();

    protected override string DescribeContent(ErConfiguration? configuration)
    {
        if (configuration?.Format is not { } format) return "No format.";

        var components = CountComponents(format.Root);
        var mapping = format.Mapping;
        var disabled = mapping?.BindingsByComponent.Values
                              .SelectMany(b => b)
                              .Count(b => b.IsDisabling) ?? 0;

        return $"{components} components  ·  {mapping?.BindingCount ?? 0} bindings  ·  {disabled} disabled";
    }

    protected override IEnumerable<TreeSectionViewModel> BuildSections(
        ErConfiguration? configuration, PaneOption? option)
    {
        _references.Clear();
        _deepReferences.Clear();
        _sourcesByPath.Clear();

        var formatSection  = Section("Format", PaneMarker.Format);
        var mappingSection = Section("Format mapping", PaneMarker.FormatMapping);

        if (configuration?.Format is not { } format)
            return new[] { formatSection, mappingSection };

        if (format.Root is not null)
        {
            var formatNode = new TreeNodeViewModel
            {
                Header  = format.Name ?? "Format",
                Badge   = "Format",
                Detail  = format.Description,
                Path    = format.Name,
                Tooltip = format.Id
            };
            formatNode.Children.Add(ComponentNode(format.Root, format.Mapping, format.Name ?? ""));
            formatNode.IsExpanded = true;
            formatSection.Nodes.Add(formatNode);

            // Resolving every row once here keeps the reverse lookup from re-walking the whole
            // data source graph for each of the thousand-odd components.
            if (format.Mapping is not null)
                foreach (var row in formatNode.DescendantsAndSelf())
                {
                    _references[row]     = ResolveAll(row, format.Mapping);
                    _deepReferences[row] = ResolveDeep(row, format.Mapping);
                }
        }

        if (format.Mapping is { } mapping)
        {
            var mappingNode = new TreeNodeViewModel
            {
                Header  = mapping.Name ?? "Format mapping",
                Badge   = "Format mapping",
                Detail  = TextUtil.Join(
                              mapping.FormatVersion is null ? null : $"format version: {mapping.FormatVersion}",
                              $"{mapping.Datasources.Sum(CountSources)} data sources"),
                Path    = mapping.Name,
                Tooltip = mapping.Id
            };

            foreach (var source in TreeSort.Sorted(mapping.Datasources, d => d.Name))
                mappingNode.Children.Add(
                    ModelMappingPaneViewModel.DatasourceNode(source, _sourcesByPath, Labels));

            mappingNode.IsExpanded = true;
            mappingSection.Nodes.Add(mappingNode);
        }

        return new[] { formatSection, mappingSection };
    }

    private static TreeNodeViewModel ComponentNode(
        ErFormatComponent component, ErFormatMappingInfo? mapping, string parentPath)
    {
        List<ErComponentBinding> bindings = component.Id is { } id &&
                                            mapping is not null &&
                                            mapping.BindingsByComponent.TryGetValue(id, out var found)
            ? found
            : new List<ErComponentBinding>();

        var value    = bindings.FirstOrDefault(b => b.IsValueBinding);
        var enabled  = bindings.FirstOrDefault(b => b.IsEnabledBinding);
        var disabled = enabled?.IsDisabling ?? false;

        var name = CaptionFor(component, value);
        var path = parentPath.Length == 0 ? name : $"{parentPath}/{name}";

        var node = new TreeNodeViewModel
        {
            Header = name,
            Badge  = BadgeFor(component.Kind),
            Detail = TextUtil.Join(
                         // The literal @Value on an attribute, when nothing is bound to it.
                         value is null && component.Name is not null ? Quote(component.Value) : null,
                         // A named cell still wants its address shown.
                         component.Name is not null ? component.ExcelRange : null,
                         component.ReplicationDirection is null ? null
                             : $"replicate {component.ReplicationDirection}",
                         component.Delimiter is null ? null : $"delimiter {Quote(component.Delimiter)}",
                         component.DataType,
                         TextUtil.OneLine(value?.Expression),
                         component.DateFormat,
                         disabled          ? "disabled"
                         : enabled is null ? null
                                           : $"if {TextUtil.OneLine(enabled.Expression, 60)}"),
            Path            = path,
            Expression      = value?.Expression,
            Condition       = enabled?.Expression,
            Value           = component.Value,
            Payload         = component,
            ReferencedPaths = ReferencedBy(bindings),
            Tooltip    = TextUtil.Join(
                             component.Id?.ToString(),
                             value?.Expression,
                             enabled is null ? null : $"Enabled: {enabled.Expression}"),
            IsDimmed = disabled
        };

        // Deliberately not sorted. The order of format components *is* the output: XML elements
        // are emitted in this sequence, and a sorted view would describe a document the format
        // never produces.
        foreach (var child in component.Children)
            node.Children.Add(ComponentNode(child, mapping, path));

        return node;
    }

    /// <summary>
    /// Every data source path this component reads, across all of its property bindings.
    /// <para>
    /// The value binding is the obvious one, but an Enabled condition reads model fields just as
    /// really — a component emitted only when two currency codes differ depends on both of them.
    /// Counting only the value binding left those dependencies invisible to the dots and to the
    /// trace menus.
    /// </para>
    /// <para>
    /// The value binding comes first so that the trace submenu still leads with what the component
    /// emits, rather than with the condition that gates it.
    /// </para>
    /// </summary>
    private static IReadOnlyList<string> ReferencedBy(IEnumerable<ErComponentBinding> bindings) =>
        bindings
            .OrderByDescending(b => b.IsValueBinding)
            .SelectMany(b => b.ReferencedPaths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// What to show as the row caption. XML components carry a @Name, Excel ones mostly do not:
    /// a cell is identified by its range and a sheet by its sheet name, so without this every
    /// unnamed cell would read simply "ExcelCell".
    /// </summary>
    private static string CaptionFor(ErFormatComponent component, ErComponentBinding? value) =>
        component.Name
        ?? component.ExcelSheetName
        ?? component.ExcelRange
        ?? component.Value
        // Many components carry no name at all and are identified purely by what they emit, so
        // fall back to the bound formula rather than repeating the kind twice across the row.
        ?? TextUtil.OneLine(value?.Expression, 80)
        ?? BadgeFor(component.Kind);

    private static string? Quote(string? value) =>
        string.IsNullOrEmpty(value) ? null : $"\"{TextUtil.OneLine(value, 60)}\"";

    private static string BadgeFor(string kind) => kind switch
    {
        "XMLElement"         => "element",
        "XMLAttribute"       => "attribute",
        "String"             => "string",
        "Date"               => "date",
        "FileComponent"      => "file",
        "Base64Component"    => "base64",

        // Excel output components.
        "ExcelFileComponent" => "workbook",
        "ExcelSheet"         => "sheet",
        "ExcelRange"         => "range",
        "ExcelCell"          => "cell",
        "ExcelHeader"        => "header",
        "ExcelFooter"        => "footer",

        // Containers a format uses regardless of output kind. A folder component lets one format
        // emit several files at once — the payment sample writes XML and a workbook together.
        "FolderComponent"    => "folder",
        "Sequence"           => "sequence",
        "DataItem"           => "data item",

        _                    => kind
    };

    private static int CountComponents(ErFormatComponent? component) =>
        component is null ? 0 : 1 + component.Children.Sum(CountComponents);

    private static int CountSources(ErDatasourceNode node) => 1 + node.Children.Sum(CountSources);

    /// <summary>
    /// Every model mapping binding a format row leads to.
    /// <para>
    /// A format binding names paths like "Invoice/InvoiceBase/Id". The first segment is a data
    /// source declared in the format mapping; when it is backed by the model it carries the model
    /// GUID, revision and root descriptor — the triple that picks one mapping line out of the
    /// several in a model mapping file — and the rest of the path is the binding key.
    /// </para>
    /// <para>
    /// A path may instead land on a calculated field the <i>format</i> mapping declares over the
    /// model, such as "Invoice/$ProfileID". Those have no binding in the model mapping, so the walk
    /// follows their own formulas onward. A conditional formula depends on several model paths, so
    /// all of them are returned rather than an arbitrary first.
    /// </para>
    /// </summary>
    /// <summary>
    /// The model mapping bindings a format row leads to.
    /// <para>
    /// A format binding names paths like "Invoice/InvoiceBase/Id". The first segment is a data
    /// source declared in the format mapping; when it is backed by the model it carries the model
    /// GUID, revision and root descriptor — the triple that picks one mapping line out of the
    /// several in a model mapping file — and the rest of the path is the binding key.
    /// </para>
    /// <para>
    /// Direct references only: a path addressed through a calculated field is left where it is
    /// rather than followed to whatever that field reads.
    /// </para>
    /// </summary>
    private static IReadOnlyList<ModelBindingReference> ResolveAll(
        TreeNodeViewModel node, ErFormatMappingInfo mapping)
    {
        if (node.ReferencedPaths.Count == 0) return Array.Empty<ModelBindingReference>();

        var found = new List<ModelBindingReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var referenced in node.ReferencedPaths)
        {
            var separator = referenced.IndexOf('/');
            if (separator <= 0 || separator == referenced.Length - 1) continue;

            var datasourceName = referenced[..separator];
            var remainder      = referenced[(separator + 1)..];

            var source = mapping.Datasources.FirstOrDefault(
                d => d.Name.Equals(datasourceName, StringComparison.OrdinalIgnoreCase));

            if (source is null || !source.IsModelSource) continue;

            if (seen.Add(referenced))
                found.Add(new ModelBindingReference(
                    source.ModelGuid, source.ModelRevision, source.ModelDescriptor,
                    remainder, datasourceName));
        }

        return found;
    }

    /// <summary>Direct model references of a row, used for marking.</summary>
    public IReadOnlyList<ModelBindingReference> ReferencesFor(TreeNodeViewModel node) =>
        _references.GetValueOrDefault(node) ?? Array.Empty<ModelBindingReference>();

    /// <summary>
    /// Model references of a row including those reached through calculated fields. Used by the
    /// trace menu, where following the chain is the whole point of asking.
    /// </summary>
    public IReadOnlyList<ModelBindingReference> TraceReferencesFor(TreeNodeViewModel node) =>
        _deepReferences.GetValueOrDefault(node) ?? Array.Empty<ModelBindingReference>();

    /// <summary>
    /// Resolves a row to model references, following calculated data sources on the way.
    /// <para>
    /// A path is often addressed through something that stands for something else: one format
    /// reads <c>$FirstPO/ID</c>, where <c>$FirstPO</c> is <c>FIRSTORNULL(model.'$PurchPurchase')</c>
    /// and that is <c>model.PurchaseOrderInquiry</c>. The tail travels with each substitution, or
    /// the field being read is lost and only the record it sits in survives.
    /// </para>
    /// </summary>
    private static IReadOnlyList<ModelBindingReference> ResolveDeep(
        TreeNodeViewModel node, ErFormatMappingInfo mapping)
    {
        if (node.ReferencedPaths.Count == 0) return Array.Empty<ModelBindingReference>();

        var declared = IndexDatasources(mapping);
        var visited  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending  = new Queue<string>(node.ReferencedPaths);
        var found    = new List<ModelBindingReference>();
        var seen     = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (pending.Count > 0)
        {
            var referenced = pending.Dequeue();
            if (!visited.Add(referenced)) continue;

            if (TryRewrite(referenced, declared, out var rewritten))
            {
                foreach (var next in rewritten) pending.Enqueue(next);
                continue;
            }

            var separator = referenced.IndexOf('/');
            if (separator <= 0 || separator == referenced.Length - 1) continue;

            var datasourceName = referenced[..separator];
            var remainder      = referenced[(separator + 1)..];

            var source = mapping.Datasources.FirstOrDefault(
                d => d.Name.Equals(datasourceName, StringComparison.OrdinalIgnoreCase));

            if (source is null) continue;

            if (source.IsModelSource)
            {
                if (seen.Add(referenced))
                    found.Add(new ModelBindingReference(
                        source.ModelGuid, source.ModelRevision, source.ModelDescriptor,
                        remainder, datasourceName));

                continue;
            }

            foreach (var next in source.ResultPaths)
                pending.Enqueue($"{next}/{remainder}");
        }

        return found;
    }

    /// <summary>
    /// Rewrites a path through the longest data source along it that stands for something else.
    /// Nothing after that source means the row simply reads it, so every path its formula touches
    /// counts; something after it means only the value it evaluates to can carry the tail.
    /// </summary>
    private static bool TryRewrite(
        string path, Dictionary<string, ErDatasourceNode> declared, out List<string> rewritten)
    {
        rewritten = new List<string>();

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        for (var length = segments.Length; length > 0; length--)
        {
            var candidate = string.Join('/', segments.Take(length));

            if (!declared.TryGetValue(candidate, out var source) || source.IsSynthetic) continue;

            var tail = string.Join('/', segments.Skip(length));
            var targets = tail.Length == 0 ? source.ReferencedPaths : source.ResultPaths;
            if (targets.Count == 0) continue;

            foreach (var target in targets)
                rewritten.Add(tail.Length == 0 ? target : $"{target}/{tail}");

            return rewritten.Count > 0;
        }

        return false;
    }

    /// <summary>
    /// The reverse direction: which format rows end up reading a given model field. A path matches
    /// its own row and everything nested under it, so asking about a record answers for its fields
    /// too.
    /// </summary>
    public IReadOnlyList<(TreeNodeViewModel Row, ModelBindingReference Reference)> FindConsumers(
        string? rootDescriptor, string modelPath)
    {
        var results = new List<(TreeNodeViewModel, ModelBindingReference)>();

        foreach (var (row, references) in _references)
        foreach (var reference in references)
        {
            if (rootDescriptor is not null && reference.RootDescriptor is not null &&
                !reference.RootDescriptor.Equals(rootDescriptor, StringComparison.OrdinalIgnoreCase))
                continue;

            var path = reference.ModelPath;
            var matches = path.Equals(modelPath, StringComparison.OrdinalIgnoreCase)
                       || path.StartsWith(modelPath + "/", StringComparison.OrdinalIgnoreCase);

            if (matches) results.Add((row, reference));
        }

        return results;
    }

    /// <summary>Selects a format row and scrolls to it, opening the branches above it.</summary>
    public void SelectRow(TreeNodeViewModel row)
    {
        var root = PrimarySection?.Nodes.FirstOrDefault();
        if (root is not null) ExpandTowards(root, row);

        PrimarySection?.SelectAndReveal(row);
    }

    /// <summary>
    /// The format mapping data source rows a set of referenced paths lands in. Only declared data
    /// sources are rows, so the longest declared prefix is the one worth marking.
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

    private static Dictionary<string, ErDatasourceNode> IndexDatasources(ErFormatMappingInfo mapping)
    {
        var index = new Dictionary<string, ErDatasourceNode>(StringComparer.OrdinalIgnoreCase);

        void Walk(ErDatasourceNode node)
        {
            index[node.FullPath] = node;
            foreach (var child in node.Children) Walk(child);
        }

        foreach (var root in mapping.Datasources) Walk(root);
        return index;
    }

    /// <summary>The format mapping data sources that are backed by the model.</summary>
    public IReadOnlyList<ErDatasourceNode> ModelDatasources =>
        Configuration?.Format?.Mapping?.Datasources.Where(d => d.IsModelSource).ToList()
        ?? (IReadOnlyList<ErDatasourceNode>)Array.Empty<ErDatasourceNode>();

    /// <summary>Format rows whose formula reads this data source path, or something under it.</summary>
    public IEnumerable<TreeNodeViewModel> FindRowsReferencing(string path)
    {
        foreach (var section in new[] { PrimarySection, SecondarySection })
        foreach (var root in section?.Nodes ?? Enumerable.Empty<TreeNodeViewModel>())
        foreach (var row in root.DescendantsAndSelf())
        {
            if (row.Path is not null && row.Path.Equals(path, StringComparison.OrdinalIgnoreCase))
                continue;

            if (ModelMappingPaneViewModel.Reads(row, path)) yield return row;
        }
    }

    /// <summary>
    /// Finds the component rows named by a set of data source paths.
    /// <para>
    /// A mapping embedded in a format reads that format through a data source backed by
    /// <c>ERExportFormatDatasource</c>, and the path beneath it walks component names —
    /// "format/Document/CstmrCdtTrfInitn/PmtInf". Segments are matched against descendants rather
    /// than direct children, because the path skips the file and folder components that wrap the
    /// tree.
    /// </para>
    /// </summary>
    /// <param name="prefixes">Names of the data sources that stand for this format.</param>
    public IReadOnlyList<TreeNodeViewModel> FindComponentRows(
        IEnumerable<string> paths, IReadOnlyCollection<string> prefixes)
    {
        var found = new List<TreeNodeViewModel>();

        var root = PrimarySection?.Nodes.FirstOrDefault();
        if (root is null || prefixes.Count == 0) return found;

        foreach (var path in paths)
        {
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2) continue;
            if (!prefixes.Contains(segments[0], StringComparer.OrdinalIgnoreCase)) continue;

            var row = Descend(root, segments.Skip(1));
            if (row is not null && !found.Contains(row)) found.Add(row);
        }

        return found;
    }

    /// <summary>The deepest row reachable by following the segments; null if the first one misses.</summary>
    private static TreeNodeViewModel? Descend(TreeNodeViewModel root, IEnumerable<string> segments)
    {
        var current = root;

        foreach (var segment in segments)
        {
            var next = FindDescendant(current, segment);
            if (next is null) break;

            current = next;
        }

        return ReferenceEquals(current, root) ? null : current;
    }

    /// <summary>Breadth-first so the shallowest match wins when a name repeats deeper down.</summary>
    private static TreeNodeViewModel? FindDescendant(TreeNodeViewModel from, string name)
    {
        var queue = new Queue<TreeNodeViewModel>(from.Children);

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node.Header.Equals(name, StringComparison.OrdinalIgnoreCase)) return node;

            foreach (var child in node.Children) queue.Enqueue(child);
        }

        return null;
    }

    /// <summary>
    /// Reads a format mapping path as a model path by dropping the data source name it is
    /// addressed through — "model/AssetBalancesPeriod" is the model path "AssetBalancesPeriod".
    /// </summary>
    public ModelBindingReference? ModelPathOf(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        var separator = path.IndexOf('/');
        if (separator <= 0 || separator == path.Length - 1) return null;

        var prefix = path[..separator];

        var source = ModelDatasources.FirstOrDefault(
            d => d.Name.Equals(prefix, StringComparison.OrdinalIgnoreCase));

        return source is null
            ? null
            : new ModelBindingReference(source.ModelGuid, source.ModelRevision,
                                        source.ModelDescriptor, path[(separator + 1)..], prefix);
    }

    /// <summary>
    /// Format mapping rows standing for a model path, or for something under it.
    /// <para>
    /// A row qualifies either by being that path — its own name is addressed through the model
    /// data source — or by reading it. A group-by named <c>$RecordsByAsset</c> is not called
    /// anything like the list it groups, but it is the row that represents it.
    /// </para>
    /// </summary>
    public IEnumerable<TreeNodeViewModel> FindDatasourceRowsForModel(string? rootDescriptor, string modelPath)
    {
        TreeNodeViewModel? nearestAncestor = null;
        var ancestorDepth = -1;

        foreach (var (path, row) in _sourcesByPath)
        {
            var candidates = new[] { path }.Concat(row.ReferencedPaths).ToList();

            if (candidates.Any(c => Stands(c, rootDescriptor, modelPath)))
            {
                yield return row;
                continue;
            }

            // The row may instead sit above the path: a binding on "InvoiceBase/Id" has no row of
            // its own in the format mapping, but "Invoice/InvoiceBase" is there and is where that
            // field is reached from. Only the deepest such row is worth marking — every ancestor
            // above it would match too, and marking the whole chain says nothing.
            foreach (var candidate in candidates)
            {
                if (Covers(candidate, rootDescriptor, modelPath) is not { } depth) continue;
                if (depth <= ancestorDepth) continue;

                ancestorDepth = depth;
                nearestAncestor = row;
            }
        }

        if (nearestAncestor is not null) yield return nearestAncestor;
    }

    /// <summary>Depth of a row whose model path is a strict ancestor of <paramref name="modelPath"/>.</summary>
    private int? Covers(string path, string? rootDescriptor, string modelPath)
    {
        if (ModelPathOf(path) is not { } reference) return null;

        if (rootDescriptor is not null && reference.RootDescriptor is not null &&
            !reference.RootDescriptor.Equals(rootDescriptor, StringComparison.OrdinalIgnoreCase))
            return null;

        if (reference.ModelPath.Length == 0) return null;

        return modelPath.StartsWith(reference.ModelPath + "/", StringComparison.OrdinalIgnoreCase)
            ? reference.ModelPath.Count(c => c == '/') + 1
            : null;
    }

    private bool Stands(string path, string? rootDescriptor, string modelPath)
    {
        if (ModelPathOf(path) is not { } reference) return false;

        if (rootDescriptor is not null && reference.RootDescriptor is not null &&
            !reference.RootDescriptor.Equals(rootDescriptor, StringComparison.OrdinalIgnoreCase))
            return false;

        return reference.ModelPath.Equals(modelPath, StringComparison.OrdinalIgnoreCase)
            || reference.ModelPath.StartsWith(modelPath + "/", StringComparison.OrdinalIgnoreCase);
    }
}
