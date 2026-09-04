using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using D365ERAnalyzer.Model;
using D365ERAnalyzer.Parsing;
using Microsoft.Win32;

namespace D365ERAnalyzer.ViewModels;

/// <summary>
/// One of the three panes. Owns its loaded configuration and one or two independently scrollable
/// tree sections; subclasses decide what those sections contain.
/// </summary>
public abstract class ConfigPaneViewModel : ObservableObject
{
    private ErConfiguration? _configuration;
    private TreeNodeViewModel? _selectedNode;
    private PaneOption? _selectedOption;
    private TreeSectionViewModel? _primarySection;
    private TreeSectionViewModel? _secondarySection;
    private string _status = "No file loaded.";

    protected ConfigPaneViewModel(ErConfigKind expectedKind)
    {
        ExpectedKind = expectedKind;
        Title = expectedKind.ToDisplayName();

        OpenCommand  = new RelayCommand(OpenFile);
        CloseCommand = new RelayCommand(Close, () => _configuration is not null);
    }

    public ErConfigKind ExpectedKind { get; }
    public string Title { get; }

    /// <summary>Shared label translations, assigned by the shell before a file is loaded.</summary>
    public LabelContext Labels { get; set; } = new();

    public ObservableCollection<PaneOption> Options { get; } = new();

    public RelayCommand OpenCommand { get; }
    public RelayCommand CloseCommand { get; }

    /// <summary>Caption above the selector combo. Empty when the pane has no selector.</summary>
    public abstract string OptionLabel { get; }


    public bool HasOptions => Options.Count > 0;

    public TreeSectionViewModel? PrimarySection
    {
        get => _primarySection;
        private set => Set(ref _primarySection, value);
    }

    public TreeSectionViewModel? SecondarySection
    {
        get => _secondarySection;
        private set
        {
            if (Set(ref _secondarySection, value))
                OnPropertyChanged(nameof(HasSecondarySection));
        }
    }

    public bool HasSecondarySection => _secondarySection is not null;

    public ErConfiguration? Configuration
    {
        get => _configuration;
        private set
        {
            if (!Set(ref _configuration, value)) return;
            OnPropertyChanged(nameof(HasFile));
            OnPropertyChanged(nameof(FileName));
            OnPropertyChanged(nameof(Caption));
        }
    }

    public bool HasFile => _configuration is not null;

    public string FileName =>
        _configuration is null ? "—" : Path.GetFileName(_configuration.Envelope.FilePath);

    /// <summary>Configuration name and version, e.g. "CEZ Invoice model (322.7)".</summary>
    public string Caption => _configuration?.Envelope.RootCaption ?? "—";

    public PaneOption? SelectedOption
    {
        get => _selectedOption;
        set
        {
            if (Set(ref _selectedOption, value) && _configuration is not null)
                Rebuild();
        }
    }

    /// <summary>Last node selected in either section; drives the details fields.</summary>
    public TreeNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        private set
        {
            if (!Set(ref _selectedNode, value)) return;
            OnPropertyChanged(nameof(DetailName));
            OnPropertyChanged(nameof(DetailType));
            OnPropertyChanged(nameof(DetailPath));
            OnPropertyChanged(nameof(DetailExpression));
            OnPropertyChanged(nameof(DetailCondition));
        }
    }

    public string? DetailName       => _selectedNode?.Header;
    public string? DetailType       => _selectedNode?.Badge;
    public string? DetailPath       => _selectedNode?.Path;
    public string? DetailExpression => _selectedNode?.Expression;
    public string? DetailCondition  => _selectedNode?.Condition;

    public string Status
    {
        get => _status;
        protected set => Set(ref _status, value);
    }

    /// <summary>Set when a search could only reveal part of its hits; shown in the main status.</summary>
    public string? SearchNote { get; protected set; }

    private void OpenFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = $"Open {Title} configuration",
            Filter = "ER configuration (*.xml)|*.xml|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog() == true)
            Load(dialog.FileName);
    }

    /// <summary>
    /// Loads a file into this pane. A configuration of the wrong type is still shown — the pane
    /// says so rather than refusing, since a mis-drop is easier to see than to explain.
    /// </summary>
    public void Load(string path)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var configuration = ErConfigurationReader.Read(path);
            stopwatch.Stop();

            Configuration = configuration;
            RefreshOptions();

            Status = configuration.Envelope.Kind == ExpectedKind
                ? $"{DescribeContent(configuration)}  ·  {stopwatch.ElapsedMilliseconds} ms"
                : $"⚠ This file is a {configuration.Envelope.Kind.ToDisplayName()}, not a {Title}.";
        }
        catch (Exception ex)
        {
            Configuration = null;
            ClearSections();
            Options.Clear();
            OnPropertyChanged(nameof(HasOptions));
            Status = $"⚠ Could not read: {ex.Message}";
        }

        ContentChanged?.Invoke(this);
    }

    /// <summary>
    /// Raised whenever this pane's file changes — loaded or closed — so the shell can re-pool
    /// anything shared between panes. Closing matters as much as loading: a mapping line this
    /// pane was contributing has to stop being offered elsewhere.
    /// </summary>
    public event Action<ConfigPaneViewModel>? ContentChanged;

    /// <summary>
    /// Rebuilds the selector and the trees, keeping the current choice when it is still on offer.
    /// <para>
    /// Options do not come only from this pane's own file — the mapping pane also lists lines
    /// embedded in a format — so this has to be callable when something else changes.
    /// </para>
    /// </summary>
    public void RefreshOptions()
    {
        var previous = _selectedOption?.Payload;

        Options.Clear();
        foreach (var option in BuildOptions(_configuration))
            Options.Add(option);
        OnPropertyChanged(nameof(HasOptions));

        // Assigned through the field: the property setter would rebuild before the caller is ready.
        _selectedOption = Options.FirstOrDefault(o => ReferenceEquals(o.Payload, previous))
                          ?? Options.FirstOrDefault(o => IsDefaultOption(_configuration, o))
                          ?? Options.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedOption));

        Rebuild();
    }

    private void Close()
    {
        Configuration = null;

        // Rebuild rather than clear: the selector may still hold mapping lines contributed by a
        // model or a format, and those have nothing to do with the file being closed.
        RefreshOptions();

        Status = Options.Count == 0
            ? "No file loaded."
            : $"No file loaded  ·  {Options.Count} line(s) from other files";

        ContentChanged?.Invoke(this);
    }

    private void ClearSections()
    {
        Unsubscribe(PrimarySection);
        Unsubscribe(SecondarySection);
        PrimarySection = null;
        SecondarySection = null;
        SelectedNode = null;
    }

    private void Unsubscribe(TreeSectionViewModel? section)
    {
        if (section is null) return;

        section.SelectionChanged -= OnSectionSelectionChanged;
        section.ContextRequested -= OnSectionContextRequested;
    }

    private void OnSectionContextRequested(TreeSectionViewModel section, TreeNodeViewModel node) =>
        NodeContextRequested?.Invoke(this, section, node);

    /// <summary>Raised after any row in this pane is selected.</summary>
    public event Action<ConfigPaneViewModel, TreeSectionViewModel, TreeNodeViewModel>? NodeSelected;

    /// <summary>Raised when a row is right-clicked, which does not change the selection.</summary>
    public event Action<ConfigPaneViewModel, TreeSectionViewModel, TreeNodeViewModel>? NodeContextRequested;

    /// <summary>Raised after the tree sections are rebuilt, e.g. on a new mapping line.</summary>
    public event Action<ConfigPaneViewModel>? SectionsRebuilt;

    private void OnSectionSelectionChanged(TreeSectionViewModel section, TreeNodeViewModel? node)
    {
        if (node is null) return;

        SelectedNode = node;
        OnSelectionChanged(section, node);
        NodeSelected?.Invoke(this, section, node);
    }

    /// <summary>
    /// Hook for panes that react to selection beyond filling the details fields. The section is
    /// passed because behaviour differs by section: selecting a binding recomputes the source
    /// highlight, while selecting one of the highlighted sources must leave it alone.
    /// </summary>
    protected virtual void OnSelectionChanged(TreeSectionViewModel section, TreeNodeViewModel node) { }

    /// <summary>Rebuilds the trees, e.g. after the display language changes.</summary>
    public void RebuildTree()
    {
        if (_configuration is not null || Options.Count > 0) Rebuild();
    }

    private void Rebuild()
    {
        ClearSections();

        // A pane can have content without a file of its own: the mapping pane shows lines embedded
        // in a format even when no model mapping has been opened.
        if (_configuration is null && Options.Count == 0) return;

        var sections = BuildSections(_configuration, _selectedOption).ToList();

        foreach (var section in sections)
        {
            section.SelectionChanged += OnSectionSelectionChanged;
            section.ContextRequested += OnSectionContextRequested;
        }

        PrimarySection   = sections.ElementAtOrDefault(0);
        SecondarySection = sections.ElementAtOrDefault(1);

        SectionsRebuilt?.Invoke(this);
    }

    // ---- subclass hooks ----

    protected abstract IEnumerable<PaneOption> BuildOptions(ErConfiguration? configuration);

    protected abstract IEnumerable<TreeSectionViewModel> BuildSections(
        ErConfiguration? configuration, PaneOption? option);

    protected abstract string DescribeContent(ErConfiguration? configuration);

    /// <summary>Which option to preselect on load. Defaults to the first.</summary>
    protected virtual bool IsDefaultOption(ErConfiguration? configuration, PaneOption option) => false;

    /// <summary>Creates a section wired to report expand-budget overruns into the pane status.</summary>
    protected TreeSectionViewModel Section(string title, PaneMarker marker) =>
        new(title, marker, message => Status = message);

    // ---- search ----

    /// <summary>
    /// Highlights matches, then opens the branches leading to them and closes the rest.
    /// <para>
    /// Only visits nodes already materialised: a match inside an unopened lazy branch of the data
    /// model cannot be seen yet. M3 replaces this with an index over the parsed configuration.
    /// </para>
    /// </summary>
    public virtual int ApplySearch(string? term, bool exactMatch)
    {
        var hasTerm = !string.IsNullOrWhiteSpace(term);
        var hits = 0;

        foreach (var section in Sections())
        foreach (var root in section.Nodes)
        {
            foreach (var node in root.DescendantsAndSelf())
            {
                var match = hasTerm && Matches(node, term!, exactMatch);
                node.IsMatch = match;
                if (match) hits++;
            }

            if (hasTerm) SyncExpansionToMatches(root);
        }

        return hits;
    }

    protected IEnumerable<TreeSectionViewModel> Sections()
    {
        if (PrimarySection is not null) yield return PrimarySection;
        if (SecondarySection is not null) yield return SecondarySection;
    }

    private static bool Matches(TreeNodeViewModel node, string term, bool exactMatch)
    {
        if (!exactMatch)
            return Contains(node.Header, term)
                || Contains(node.Detail, term)
                || Contains(node.Badge, term)
                || Contains(node.Path, term);

        // Exact mode compares whole identifiers, so "$CustTrans" no longer drags in
        // "$CustTrans_OrderBy". Path segments count as identifiers too.
        return Equals(node.Header, term)
            || Equals(node.Path, term)
            || SegmentEquals(node.Path, term);
    }

    private static bool Contains(string? text, string term) =>
        text is not null && text.Contains(term, StringComparison.OrdinalIgnoreCase);

    private static bool Equals(string? text, string term) =>
        text is not null && text.Equals(term, StringComparison.OrdinalIgnoreCase);

    private static bool SegmentEquals(string? path, string term)
    {
        if (path is null) return false;

        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            if (segment.Equals(term, StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }

    /// <summary>
    /// Opens every branch containing a match and closes every branch that does not, so the tree
    /// tracks the search as it is typed.
    /// </summary>
    /// <returns>True when this subtree contains a match.</returns>
    private static bool SyncExpansionToMatches(TreeNodeViewModel node)
    {
        var childHit = false;

        foreach (var child in node.Children)
            if (SyncExpansionToMatches(child))
                childHit = true;

        if (node.Children.Count > 0)
            node.IsExpanded = childHit;

        return childHit || node.IsMatch;
    }
}
