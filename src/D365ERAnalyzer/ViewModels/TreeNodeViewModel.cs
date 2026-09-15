using System.Collections.ObjectModel;

namespace D365ERAnalyzer.ViewModels;

/// <summary>
/// One row in a configuration tree, used for descriptors, data sources, bindings and format
/// components alike.
/// <para>
/// Children can be supplied lazily. The data model is a cyclic graph, so its tree is materialised
/// only as far as the user opens it; a placeholder child keeps the expander visible until then.
/// </para>
/// </summary>
public sealed class TreeNodeViewModel : ObservableObject
{
    private static readonly TreeNodeViewModel Placeholder = new() { Header = "…" };

    private Func<IEnumerable<TreeNodeViewModel>>? _childFactory;
    private bool _childrenLoaded;

    private bool _isExpanded;
    private bool _isSelected;
    private bool _isMatch;
    private bool _isVisible = true;
    private bool _isNavigationTarget;
    private bool _isContextTarget;

    public TreeNodeViewModel()
    {
        // Parent links maintained automatically: children get added from a dozen places, and every
        // one of them would otherwise have to remember to set it.
        Children.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is null) return;

            foreach (TreeNodeViewModel child in e.NewItems)
                child.Parent = this;
        };
    }

    public TreeNodeViewModel? Parent { get; private set; }

    public required string Header { get; init; }

    /// <summary>Secondary muted text shown after the header (binding, path, join keys…).</summary>
    public string? Detail { get; init; }

    /// <summary>Short node-type tag rendered as a pill, e.g. "list", "Table", "Enabled".</summary>
    public string? Badge { get; init; }

    public string? Tooltip { get; init; }

    // ---- fields surfaced in the pane details panel, untruncated ----

    /// <summary>Full path of this node within its configuration, when it has one.</summary>
    public string? Path { get; init; }

    /// <summary>The complete formula, with its original line breaks intact.</summary>
    public string? Expression { get; init; }

    /// <summary>A secondary formula, e.g. the Enabled condition of a format component.</summary>
    public string? Condition { get; init; }

    /// <summary>
    /// A literal typed into the component itself rather than bound — the <c>@Value</c> of an XML
    /// attribute or a string. Usually present exactly when there is no formula, and then it is
    /// the only place the emitted text can be read.
    /// </summary>
    public string? Value { get; init; }

    /// <summary>Data source paths this row's formula refers to.</summary>
    public IReadOnlyList<string> ReferencedPaths { get; init; } = Array.Empty<string>();

    /// <summary>The domain object behind this row, for commands that need more than the caption.</summary>
    public object? Payload { get; init; }

    /// <summary>Renders the row at reduced opacity — used for disabled format components.</summary>
    public bool IsDimmed { get; init; }

    public ObservableCollection<TreeNodeViewModel> Children { get; } = new();

    public bool CanExpand => _childFactory is not null || Children.Count > 0;

    /// <summary>Attaches a lazily-evaluated child set. Call instead of filling <see cref="Children"/>.</summary>
    public void SetLazyChildren(Func<IEnumerable<TreeNodeViewModel>> factory)
    {
        _childFactory = factory;
        Children.Clear();
        Children.Add(Placeholder);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (value) EnsureChildrenLoaded();
            Set(ref _isExpanded, value);
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    /// <summary>Set by the search box; drives the highlight trigger in the item template.</summary>
    public bool IsMatch
    {
        get => _isMatch;
        set => Set(ref _isMatch, value);
    }

    /// <summary>
    /// Dots shown at the head of the row, one per pane whose current selection refers to it.
    /// </summary>
    public ObservableCollection<RowMarker> Markers { get; } = new();

    public void AddMarker(PaneMarker source, MarkerDirection direction)
    {
        if (Markers.Any(m => m.Matches(source, direction))) return;

        Markers.Add(new RowMarker { Source = source, Direction = direction });
    }

    /// <summary>Removes every dot a given section put on this row, in either direction.</summary>
    public void RemoveMarkers(PaneMarker source)
    {
        for (var i = Markers.Count - 1; i >= 0; i--)
            if (ReferenceEquals(Markers[i].Source, source))
                Markers.RemoveAt(i);
    }

    /// <summary>
    /// The dotted row the up/down walk last landed on. Outlined in the tree so it can be found at
    /// a glance — the walk only scrolls, so nothing else would say where it stopped.
    /// </summary>
    public bool IsNavigationTarget
    {
        get => _isNavigationTarget;
        set => Set(ref _isNavigationTarget, value);
    }

    /// <summary>
    /// The row a context menu was opened on. Outlined so it is obvious which row the menu belongs
    /// to, without selecting it — selecting would recompute the dots and the details panel, which
    /// is a lot of movement for what is only a request to see a menu.
    /// </summary>
    public bool IsContextTarget
    {
        get => _isContextTarget;
        set => Set(ref _isContextTarget, value);
    }

    /// <summary>Cleared by a section's dot filter to hide rows unrelated to the current marks.</summary>
    public bool IsVisible
    {
        get => _isVisible;
        set => Set(ref _isVisible, value);
    }

    /// <summary>Opens every branch above this row so it is visible.</summary>
    public void RevealAncestors()
    {
        for (var parent = Parent; parent is not null; parent = parent.Parent)
            parent.IsExpanded = true;
    }

    private void EnsureChildrenLoaded()
    {
        if (_childrenLoaded || _childFactory is null) return;

        _childrenLoaded = true;
        Children.Clear();
        foreach (var child in _childFactory())
            Children.Add(child);

        // A factory that yields nothing leaves a node that claimed to be expandable; drop the
        // claim so the expander disappears rather than opening onto nothing.
        if (Children.Count == 0) _childFactory = null;
        OnPropertyChanged(nameof(CanExpand));
    }

    /// <summary>Materialised descendants only — never forces a lazy factory to run.</summary>
    public IEnumerable<TreeNodeViewModel> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in Children)
            foreach (var node in child.DescendantsAndSelf())
                yield return node;
    }

    /// <summary>
    /// Expands or collapses this subtree, spending at most <paramref name="budget"/> nodes.
    /// The budget is not optional politeness: expanding the whole model graph would materialise
    /// an enormous tree (bounded only by the cycle guard) and freeze the UI.
    /// </summary>
    /// <returns>Remaining budget; zero means the walk was cut short.</returns>
    public int SetExpandedRecursively(bool expanded, int budget)
    {
        if (budget <= 0) return 0;

        // Every visited node costs, leaves included. Charging only expandable nodes would let a
        // single node with a thousand children through for one unit, which defeats the point.
        budget--;

        if (!expanded)
        {
            IsExpanded = false;
            // Collapsing must not materialise anything, so only walk what already exists.
            foreach (var child in Children)
                budget = child.SetExpandedRecursively(false, budget);
            return budget;
        }

        if (!CanExpand) return budget;

        IsExpanded = true;

        foreach (var child in Children)
            budget = child.SetExpandedRecursively(true, budget);

        return budget;
    }
}
