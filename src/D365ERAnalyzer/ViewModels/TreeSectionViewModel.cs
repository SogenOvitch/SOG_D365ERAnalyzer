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

    public TreeSectionViewModel(string title, PaneMarker marker, Action<string>? notify = null)
    {
        Title = title;
        Marker = marker;
        _notify = notify;

        ExpandAllCommand   = new RelayCommand(() => SetExpanded(true),  () => Nodes.Count > 0);
        CollapseAllCommand = new RelayCommand(() => SetExpanded(false), () => Nodes.Count > 0);
        ToggleFilterCommand = new RelayCommand(() => IsFiltered = !IsFiltered, () => Nodes.Count > 0);

        NextMarkedCommand     = new RelayCommand(() => StepMarked(1),  HasMarkedRows);
        PreviousMarkedCommand = new RelayCommand(() => StepMarked(-1), HasMarkedRows);
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
    public RelayCommand GoToSelectedCommand { get; }

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
    /// Walks the dotted rows without changing the selection.
    /// <para>
    /// Deliberately scroll-only: selecting a row recomputes which rows are dotted, so stepping
    /// through the dots by selecting them would destroy the very set being walked. The selection
    /// stays on the row that produced the dots, and <see cref="GoToSelectedCommand"/> returns to it.
    /// </para>
    /// </summary>
    private void StepMarked(int direction)
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

            if (candidate.Markers.Count == 0) continue;

            SetNavigationTarget(candidate);
            candidate.RevealAncestors();
            BringIntoView?.Invoke(candidate);
            return;
        }
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
