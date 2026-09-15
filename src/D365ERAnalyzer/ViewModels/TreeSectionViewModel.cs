using System.Collections.ObjectModel;

namespace D365ERAnalyzer.ViewModels;

/// <summary>
/// One independently scrollable tree inside a pane. A pane shows one or two of these — data
/// sources above bindings, the format above its format mapping — so each can be scrolled and
/// expanded without disturbing the other.
/// </summary>
public sealed class TreeSectionViewModel : ObservableObject
{
    /// <summary>
    /// Node ceiling for "expand all", charged per visited node.
    /// <para>
    /// The trees scroll by measuring their whole content rather than estimating it, which means no
    /// virtualisation and a real container per expanded row. 6 000 comfortably covers every tree
    /// here — the largest single model root expands to 5 151 rows, the bindings tree to 1 264 —
    /// while stopping "expand all" on the data model root, which would otherwise walk 85 000.
    /// </para>
    /// </summary>
    private const int ExpandBudget = 6_000;

    private readonly Action<string>? _notify;
    private TreeNodeViewModel? _selectedNode;
    private bool _isFiltered;
    private TreeNodeViewModel? _navTarget;
    private int _matchCount;

    public TreeSectionViewModel(string title, PaneMarker marker, Action<string>? notify = null)
    {
        Title = title;
        Marker = marker;
        _notify = notify;

        ExpandAllCommand   = new RelayCommand(() => SetExpanded(true),  () => Nodes.Count > 0);
        CollapseAllCommand = new RelayCommand(() => SetExpanded(false), () => Nodes.Count > 0);
        ToggleFilterCommand = new RelayCommand(() => IsFiltered = !IsFiltered, () => Nodes.Count > 0);

        NextMarkedCommand     = new RelayCommand(() => Step(1,  IsMarked), HasMarkedRows);
        PreviousMarkedCommand = new RelayCommand(() => Step(-1, IsMarked), HasMarkedRows);
        NextMatchCommand      = new RelayCommand(() => Step(1,  IsMatch),  () => HasMatches);
        PreviousMatchCommand  = new RelayCommand(() => Step(-1, IsMatch),  () => HasMatches);
        GoToSelectedCommand   = new RelayCommand(GoToSelected, () => SelectedNode is not null);
    }

    public string Title { get; }

    /// <summary>This section's colour, shown by its title and on rows its selection refers to.</summary>
    public PaneMarker Marker { get; }

    public ObservableCollection<TreeNodeViewModel> Nodes { get; } = new();

    public RelayCommand ExpandAllCommand { get; }
    public RelayCommand CollapseAllCommand { get; }
    public RelayCommand ToggleFilterCommand { get; }

    public RelayCommand NextMarkedCommand { get; }
    public RelayCommand PreviousMarkedCommand { get; }
    public RelayCommand NextMatchCommand { get; }
    public RelayCommand PreviousMatchCommand { get; }
    public RelayCommand GoToSelectedCommand { get; }

    /// <summary>Rows in this section matching the search box; zero when the box is empty.</summary>
    public int MatchCount
    {
        get => _matchCount;
        set
        {
            if (!Set(ref _matchCount, value)) return;

            OnPropertyChanged(nameof(HasMatches));
            OnPropertyChanged(nameof(MatchTooltip));
        }
    }

    /// <summary>Shows the search walk buttons, which mean nothing without a hit to walk.</summary>
    public bool HasMatches => _matchCount > 0;

    public string MatchTooltip => $"{_matchCount} search match(es) in this section";

    /// <summary>
    /// When set, the section shows only dotted rows together with their ancestors and descendants,
    /// so a marked row keeps the context that explains where it sits.
    /// </summary>
    public bool IsFiltered
    {
        get => _isFiltered;
        set
        {
            if (!Set(ref _isFiltered, value)) return;

            OnPropertyChanged(nameof(FilterTooltip));
            RefreshFilter();
        }
    }

    public string FilterTooltip => _isFiltered
        ? "Showing only dotted rows and their context — click to show everything"
        : "Show only dotted rows and their context";

    /// <summary>
    /// Walks the dotted rows, or the search matches, without changing the selection.
    /// <para>
    /// Deliberately scroll-only: selecting a row recomputes which rows are dotted, so stepping
    /// through the dots by selecting them would destroy the very set being walked. The selection
    /// stays on the row that produced the dots, and <see cref="GoToSelectedCommand"/> returns to it.
    /// The search walk behaves the same way so that the two arrow pairs are one gesture, and so
    /// that paging through hits does not drag every other pane's dots along with it.
    /// </para>
    /// <para>
    /// Both walks share one position, so switching from the dots to the matches carries on from
    /// the row last landed on instead of jumping back to the top.
    /// </para>
    /// </summary>
    private void Step(int direction, Func<TreeNodeViewModel, bool> wanted)
    {
        var rows = Nodes.SelectMany(n => n.DescendantsAndSelf()).ToList();
        if (rows.Count == 0) return;

        // Continue from where the walk stopped, or from the selected row when it has not started.
        var origin = _navTarget is not null ? rows.IndexOf(_navTarget)
                   : SelectedNode is not null ? rows.IndexOf(SelectedNode)
                   : -1;

        for (var step = 1; step <= rows.Count; step++)
        {
            var index = ((origin + direction * step) % rows.Count + rows.Count) % rows.Count;
            var candidate = rows[index];

            // A row the dot filter hides cannot be scrolled to; landing on it would look like the
            // button did nothing.
            if (!wanted(candidate) || !IsShown(candidate)) continue;

            SetNavigationTarget(candidate);
            candidate.RevealAncestors();
            BringIntoView?.Invoke(candidate);
            return;
        }
    }

    private static bool IsMarked(TreeNodeViewModel node) => node.Markers.Count > 0;

    private static bool IsMatch(TreeNodeViewModel node) => node.IsMatch;

    private static bool IsShown(TreeNodeViewModel node)
    {
        for (var current = node; current is not null; current = current.Parent)
            if (!current.IsVisible) return false;

        return true;
    }

    private void SetNavigationTarget(TreeNodeViewModel? node)
    {
        if (_navTarget is not null) _navTarget.IsNavigationTarget = false;

        _navTarget = node;

        if (_navTarget is not null) _navTarget.IsNavigationTarget = true;
    }

    private void GoToSelected()
    {
        if (SelectedNode is null) return;

        SelectedNode.RevealAncestors();
        BringIntoView?.Invoke(SelectedNode);
    }

    private bool HasMarkedRows() =>
        Nodes.SelectMany(n => n.DescendantsAndSelf()).Any(n => n.Markers.Count > 0);

    /// <summary>
    /// Called whenever the dots change. Resets the walk position — a cursor into the old dot set
    /// means nothing — and re-applies the filter if one is on, so the filtered view tracks the
    /// current selection instead of freezing on the marks present when it was switched on.
    /// </summary>
    public void OnMarkersChanged()
    {
        SetNavigationTarget(null);
        if (_isFiltered) RefreshFilter();
    }

    /// <summary>Recomputes row visibility for the dot filter.</summary>
    public void RefreshFilter()
    {

        foreach (var root in Nodes)
        {
            if (_isFiltered) ApplyFilter(root, ancestorMarked: false);
            else ShowAll(root);
        }
    }

    private static bool ApplyFilter(TreeNodeViewModel node, bool ancestorMarked)
    {
        var selfMarked = node.Markers.Count > 0;
        var descendantMarked = false;

        foreach (var child in node.Children)
            if (ApplyFilter(child, ancestorMarked || selfMarked))
                descendantMarked = true;

        node.IsVisible = selfMarked || ancestorMarked || descendantMarked;
        return selfMarked || descendantMarked;
    }

    private static void ShowAll(TreeNodeViewModel node)
    {
        node.IsVisible = true;
        foreach (var child in node.Children) ShowAll(child);
    }

    /// <summary>Raised so the owning pane can mirror the selection into its details fields.</summary>
    public event Action<TreeSectionViewModel, TreeNodeViewModel?>? SelectionChanged;

    /// <summary>Asks the view to scroll a row into view — vertically only.</summary>
    public event Action<TreeNodeViewModel>? BringIntoView;

    /// <summary>A context menu was opened on a row, without it becoming the selection.</summary>
    public event Action<TreeSectionViewModel, TreeNodeViewModel>? ContextRequested;

    public void RequestContext(TreeNodeViewModel node) => ContextRequested?.Invoke(this, node);

    public TreeNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (!Set(ref _selectedNode, value)) return;

            // A new selection restarts the walk from there.
            SetNavigationTarget(null);
            SelectionChanged?.Invoke(this, value);
        }
    }

    /// <summary>
    /// Re-announces a row that is already selected in this section.
    /// <para>
    /// With two sections in a pane, each keeps its own TreeView selection, so clicking back on the
    /// row that is still highlighted here raises no SelectedItemChanged — and the pane details
    /// would go on showing the row picked in the other section.
    /// </para>
    /// </summary>
    public void Reselect(TreeNodeViewModel node)
    {
        if (!ReferenceEquals(_selectedNode, node))
        {
            SelectedNode = node;
            return;
        }

        SetNavigationTarget(null);
        SelectionChanged?.Invoke(this, node);
    }

    /// <summary>
    /// Selects a row and scrolls to it, clearing whatever was selected before. Used by the jumps
    /// between panes, where the previous selection would otherwise stay highlighted and the
    /// scrollbar would not move.
    /// </summary>
    public void SelectAndReveal(TreeNodeViewModel node)
    {
        foreach (var root in Nodes)
            foreach (var other in root.DescendantsAndSelf())
                if (!ReferenceEquals(other, node))
                    other.IsSelected = false;

        node.IsSelected = true;
        SelectedNode = node;
        BringIntoView?.Invoke(node);
    }

    /// <summary>Expands or collapses under the selected node, or the whole section if none.</summary>
    private void SetExpanded(bool expanded)
    {
        var targets = SelectedNode is not null ? new[] { SelectedNode } : Nodes.ToArray();

        var budget = ExpandBudget;
        foreach (var target in targets)
            budget = target.SetExpandedRecursively(expanded, budget);

        if (expanded && budget <= 0)
            _notify?.Invoke($"⚠ {Title}: stopped after {ExpandBudget:N0} nodes — expand a smaller subtree.");
    }
}
