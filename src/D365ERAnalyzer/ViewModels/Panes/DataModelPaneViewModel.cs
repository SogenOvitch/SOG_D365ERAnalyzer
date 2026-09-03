using D365ERAnalyzer.Model;
using D365ERAnalyzer.Parsing;

namespace D365ERAnalyzer.ViewModels.Panes;

/// <summary>
/// Renders a data model as a single section: every root descriptor sits under the configuration
/// node. The descriptors are a flat, potentially cyclic graph, so branches are built lazily and
/// each carries the chain of descriptors above it as a cycle guard.
/// </summary>
public sealed class DataModelPaneViewModel : ConfigPaneViewModel
{
    /// <summary>
    /// How many indexed hits a search will open in the tree. Revealing every hit for a broad term
    /// would materialise thousands of nodes; the index reports the true total either way.
    /// </summary>
    private const int RevealLimit = 250;

    private ModelSearchIndex? _index;

    public DataModelPaneViewModel() : base(ErConfigKind.DataModel) { }

    /// <summary>No selector: all roots are in the tree.</summary>
    public override string OptionLabel => "";

    protected override IEnumerable<PaneOption> BuildOptions(ErConfiguration configuration) =>
        Enumerable.Empty<PaneOption>();

    protected override string DescribeContent(ErConfiguration configuration)
    {
        if (configuration.DataModel is not { } model) return "No model.";

        var roots = model.Roots.Count();
        var enums = model.Descriptors.Values.Count(d => d.IsEnum);
        var items = model.Descriptors.Values.Sum(d => d.Items.Count);
        return $"{model.Descriptors.Count} descriptors  ·  {roots} roots  ·  {enums} enums  ·  {items} fields";
    }

    protected override IEnumerable<TreeSectionViewModel> BuildSections(
        ErConfiguration configuration, PaneOption? option)
    {
        var section = Section("Data model", PaneMarker.DataModel);
        _index = null;

        if (configuration.DataModel is not { } model) return new[] { section };

        _index = ModelSearchIndex.Build(model);

        var envelope = configuration.Envelope;
        var root = new TreeNodeViewModel
        {
            Header  = envelope.RootCaption,
            Badge   = envelope.Kind.ToDisplayName(),
            Detail  = TextUtil.Join(
                          envelope.BaseName is null ? null : $"base: {envelope.BaseName}",
                          envelope.CountryRegionCodes,
                          envelope.Description),
            Path    = envelope.FilePath,
            Tooltip = envelope.FilePath
        };

        foreach (var descriptor in model.Roots)
            root.Children.Add(RootNode(model, descriptor));

        root.IsExpanded = true;
        section.Nodes.Add(root);
        return new[] { section };
    }

    private static TreeNodeViewModel RootNode(ErDataModel model, ErDescriptor descriptor)
    {
        var chain = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { descriptor.Name };

        var node = new TreeNodeViewModel
        {
            Header  = descriptor.Name,
            Badge   = descriptor.IsEnum ? "enum" : "record",
            Detail  = TextUtil.Join(descriptor.Label, descriptor.Description),
            Path    = descriptor.Name,
            Tooltip = TextUtil.Join(descriptor.Description, $"{descriptor.Items.Count} fields")
        };

        if (descriptor.Items.Count > 0)
            node.SetLazyChildren(() => descriptor.Items.Select(
                item => ItemNode(model, item, chain, descriptor.Name)));

        return node;
    }

    private static TreeNodeViewModel ItemNode(
        ErDataModel model, ErDescriptorItem item, IReadOnlyCollection<string> chain, string parentPath)
    {
        var target = model.Find(item.TypeDescriptor);

        // A descriptor already open above this point would recurse for ever.
        var cyclic = target is not null && chain.Contains(target.Name);

        var typeName = item.Type == ErDataType.Unknown && item.RawType != 0
            ? $"type {item.RawType}"
            : item.Type.ToDisplayName();

        var path = $"{parentPath}/{item.Name}";

        var node = new TreeNodeViewModel
        {
            Header  = item.Name,
            Badge   = typeName,
            Detail  = TextUtil.Join(
                          item.TypeDescriptor,
                          cyclic ? "↻ recursive — already open above" : null,
                          item.Label),
            Path    = path,
            Tooltip = TextUtil.Join(item.Description, item.Label, item.TypeDescriptor)
        };

        if (target is not null && !cyclic && target.Items.Count > 0)
        {
            var branch = new HashSet<string>(chain, StringComparer.OrdinalIgnoreCase) { target.Name };
            node.SetLazyChildren(() => target.Items.Select(
                child => ItemNode(model, child, branch, path)));
        }

        return node;
    }

    /// <summary>
    /// Searches the whole model, not just the part already on screen.
    /// <para>
    /// The tree is lazy, so a plain walk would only ever find opened branches. The index knows
    /// every field in the graph, so hits are located there first and their paths opened; the base
    /// class then highlights them and closes everything else.
    /// </para>
    /// </summary>
    public override int ApplySearch(string? term, bool exactMatch)
    {
        SearchNote = null;

        if (_index is not null && !string.IsNullOrWhiteSpace(term))
        {
            var paths = _index.FindPaths(term, exactMatch, RevealLimit, out var total);

            foreach (var path in paths)
                RevealPath(path);

            if (total > paths.Count)
                SearchNote = $"model: showing {paths.Count} of {total} matches";
        }

        return base.ApplySearch(term, exactMatch);
    }

    /// <summary>
    /// Opens the tree along a "Root/Field/Field" path, materialising each lazy level as it goes.
    /// </summary>
    private void RevealPath(string path)
    {
        var current = PrimarySection?.Nodes.FirstOrDefault();
        if (current is null) return;

        current.IsExpanded = true;

        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var next = current.Children.FirstOrDefault(
                c => c.Header.Equals(segment, StringComparison.OrdinalIgnoreCase));

            if (next is null) return;

            // Expanding materialises the next level, so the following segment can be found.
            next.IsExpanded = true;
            current = next;
        }
    }
}
