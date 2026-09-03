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
    /// <summary>Every model path each format row leads to, resolved once when the tree is built.</summary>
    private readonly Dictionary<TreeNodeViewModel, IReadOnlyList<ModelBindingReference>> _references = new();

    /// <summary>Format mapping data source rows by full path, for the green highlight.</summary>
    private readonly Dictionary<string, TreeNodeViewModel> _sourcesByPath =
        new(StringComparer.OrdinalIgnoreCase);

    public FormatPaneViewModel() : base(ErConfigKind.Format) { }

    public override string OptionLabel => "";

    protected override IEnumerable<PaneOption> BuildOptions(ErConfiguration configuration) =>
        Enumerable.Empty<PaneOption>();

    protected override string DescribeContent(ErConfiguration configuration)
    {
        if (configuration.Format is not { } format) return "No format.";

        var components = CountComponents(format.Root);
        var mapping = format.Mapping;
        var disabled = mapping?.BindingsByComponent.Values
                              .SelectMany(b => b)
                              .Count(b => b.IsDisabling) ?? 0;

        return $"{components} components  ·  {mapping?.BindingCount ?? 0} bindings  ·  {disabled} disabled";
    }

    protected override IEnumerable<TreeSectionViewModel> BuildSections(
        ErConfiguration configuration, PaneOption? option)
    {
        _references.Clear();
        _sourcesByPath.Clear();

        var formatSection  = Section("Format", PaneMarker.Format);
        var mappingSection = Section("Format mapping", PaneMarker.FormatMapping);

        if (configuration.Format is not { } format)
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
                    _references[row] = ResolveAll(row, format.Mapping);
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

            foreach (var source in mapping.Datasources)
                mappingNode.Children.Add(
                    ModelMappingPaneViewModel.DatasourceNode(source, _sourcesByPath));

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

        var name = component.Name ?? component.Value ?? component.Kind;
        var path = parentPath.Length == 0 ? name : $"{parentPath}/{name}";

        var node = new TreeNodeViewModel
        {
            Header = name,
            Badge  = BadgeFor(component.Kind),
            Detail = TextUtil.Join(
                         // The literal @Value on an attribute, when nothing is bound to it.
                         value is null && component.Name is not null ? Quote(component.Value) : null,
                         TextUtil.OneLine(value?.Expression),
                         component.DateFormat,
                         disabled          ? "disabled"
                         : enabled is null ? null
                                           : $"if {TextUtil.OneLine(enabled.Expression, 60)}"),
            Path            = path,
            Expression      = value?.Expression,
            Condition       = enabled?.Expression,
            Payload         = component,
            ReferencedPaths = value?.ReferencedPaths ?? (IReadOnlyList<string>)Array.Empty<string>(),
            Tooltip    = TextUtil.Join(
                             component.Id?.ToString(),
                             value?.Expression,
                             enabled is null ? null : $"Enabled: {enabled.Expression}"),
            IsDimmed = disabled
        };

        foreach (var child in component.Children)
            node.Children.Add(ComponentNode(child, mapping, path));

        return node;
    }

    private static string? Quote(string? value) =>
        string.IsNullOrEmpty(value) ? null : $"\"{TextUtil.OneLine(value, 60)}\"";

    private static string BadgeFor(string kind) => kind switch
    {
        "XMLElement"      => "element",
        "XMLAttribute"    => "attribute",
        "String"          => "string",
        "Date"            => "date",
        "FileComponent"   => "file",
        "Base64Component" => "base64",
        _                 => kind
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
    private static IReadOnlyList<ModelBindingReference> ResolveAll(
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

            var separator = referenced.IndexOf('/');
            if (separator <= 0 || separator == referenced.Length - 1) continue;

            var datasourceName = referenced[..separator];
            var remainder      = referenced[(separator + 1)..];

            var source = mapping.Datasources.FirstOrDefault(
                d => d.Name.Equals(datasourceName, StringComparison.OrdinalIgnoreCase));

            // Enums, CalcFunctions and friends never lead to a model binding.
            if (source is null || !source.IsModelSource) continue;

            // A node the format mapping declares over the model is a calculated field, not a model
            // field: follow its formula instead of looking for a binding that cannot exist.
            if (declared.TryGetValue(referenced, out var overlay) && !overlay.IsSynthetic)
            {
                foreach (var next in overlay.ReferencedPaths)
                    pending.Enqueue(next);

                continue;
            }

            if (seen.Add(referenced))
                found.Add(new ModelBindingReference(
                    source.ModelGuid, source.ModelRevision, source.ModelDescriptor,
                    remainder, datasourceName));
        }

        return found;
    }

    /// <summary>Model mapping targets reachable from a format row, for the context submenu.</summary>
    public IReadOnlyList<ModelBindingReference> ReferencesFor(TreeNodeViewModel node) =>
        _references.GetValueOrDefault(node) ?? Array.Empty<ModelBindingReference>();

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
}
