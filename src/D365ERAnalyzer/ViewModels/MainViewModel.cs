using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using D365ERAnalyzer.Model;
using D365ERAnalyzer.Parsing;
using D365ERAnalyzer.ViewModels.Panes;
using Microsoft.Win32;

namespace D365ERAnalyzer.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    /// <summary>Rows currently marked, per pane, so a new selection clears only its own dots.</summary>
    private readonly Dictionary<string, List<TreeNodeViewModel>> _marked = new();

    /// <summary>
    /// The live selection of each section. Kept so that when one pane rebuilds — switching mapping
    /// line, say — the dots the *other* panes were casting onto it can be laid down again on the
    /// new rows instead of vanishing.
    /// </summary>
    private readonly Dictionary<string, Selection> _selections = new();

    /// <summary>The row a context menu is currently open on, outlined but not selected.</summary>
    private TreeNodeViewModel? _contextRow;

    private sealed record Selection(
        ConfigPaneViewModel Pane, TreeSectionViewModel Section, TreeNodeViewModel Node);

    /// <summary>WPF renders at 12 by default; the multiplier is applied to that.</summary>
    private const double BaseFontSize = 12;

    private double _fontScale = 1.2;
    private string _fontScaleText = "1.2";

    private string _searchText = "";
    private bool _exactMatch;
    private bool _searchFailed;
    private string _status = "Open a folder containing exported ER configurations, or load each pane individually.";

    public MainViewModel()
    {
        ModelPane   = new DataModelPaneViewModel();
        MappingPane = new ModelMappingPaneViewModel();
        FormatPane  = new FormatPaneViewModel();

        OpenFolderCommand = new RelayCommand(OpenFolder);

        foreach (var pane in new ConfigPaneViewModel[] { ModelPane, MappingPane, FormatPane })
        {
            pane.Labels = Labels;
            pane.NodeSelected   += OnNodeSelected;
            pane.NodeContextRequested += OnNodeContextRequested;
            pane.SectionsRebuilt += OnSectionsRebuilt;
            pane.ContentChanged += OnContentChanged;
        }
    }

    public DataModelPaneViewModel ModelPane { get; }
    public ModelMappingPaneViewModel MappingPane { get; }
    public FormatPaneViewModel FormatPane { get; }

    public RelayCommand OpenFolderCommand { get; }

    /// <summary>Label translations pooled from every loaded configuration.</summary>
    public LabelContext Labels { get; } = new();

    public ObservableCollection<string> Languages { get; } = new();

    public bool HasLabels => Labels.HasLabels;

    /// <summary>
    /// Display language for label references. Changing it rebuilds the trees, since captions are
    /// resolved as the rows are built.
    /// </summary>
    public string? SelectedLanguage
    {
        get => Labels.Language;
        set
        {
            if (Labels.Language == value) return;

            Labels.Language = value;
            OnPropertyChanged();

            foreach (var pane in new ConfigPaneViewModel[] { ModelPane, MappingPane, FormatPane })
                pane.RebuildTree();
        }
    }

    /// <summary>Forward direction: format row → the model mapping bindings behind it.</summary>
    public ObservableCollection<ContextAction> TraceTargets { get; } = new();

    /// <summary>Reverse direction: format rows reading exactly this model field.</summary>
    public ObservableCollection<ContextAction> ConsumerTargets { get; } = new();

    /// <summary>
    /// Format rows reading something nested under this row. Kept apart from the direct list so a
    /// record-wide fan-out is a deliberate click rather than something you trip over while looking
    /// for the one row that reads the field itself.
    /// </summary>
    public ObservableCollection<ContextAction> NestedConsumerTargets { get; } = new();

    public bool HasTraceTargets => TraceTargets.Count > 0;
    public bool HasConsumerTargets => ConsumerTargets.Count > 0;
    public bool HasNestedConsumerTargets => NestedConsumerTargets.Count > 0;

    public bool HasAnyContextAction =>
        HasTraceTargets || HasConsumerTargets || HasNestedConsumerTargets;

    public string ConsumerHeader => $"Find format rows using this ({ConsumerTargets.Count})";

    public string NestedConsumerHeader => $"Find format rows under this ({NestedConsumerTargets.Count})";

    /// <summary>
    /// Font multiplier for the whole window, typed by the user. Applied to the window font size,
    /// which everything else inherits.
    /// </summary>
    public string FontScaleText
    {
        get => _fontScaleText;
        set
        {
            if (!Set(ref _fontScaleText, value)) return;

            // Accept both decimal separators: the box is typed into, not parsed from a file.
            var text = value.Replace(',', '.');

            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale))
                return;

            // Keep it usable: far enough either way to matter, not far enough to lose the window.
            _fontScale = Math.Clamp(scale, 0.5, 4.0);
            OnPropertyChanged(nameof(FontSize));
        }
    }

    public double FontSize => BaseFontSize * _fontScale;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value)) RunSearch();
        }
    }

    /// <summary>
    /// Matches whole identifiers instead of substrings, so "$CustTrans" stops pulling in
    /// "$CustTrans_OrderBy".
    /// </summary>
    public bool ExactMatch
    {
        get => _exactMatch;
        set
        {
            if (Set(ref _exactMatch, value)) RunSearch();
        }
    }

    /// <summary>Drives the red border on the search box.</summary>
    public bool SearchFailed
    {
        get => _searchFailed;
        private set => Set(ref _searchFailed, value);
    }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    // ------------------------------------------------------------------ loading

    private void OpenFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Open a folder of exported ER configurations" };
        if (dialog.ShowDialog() != true) return;

        LoadFolder(dialog.FolderName);
    }

    /// <summary>
    /// Loads a whole folder at once, routing each XML to the pane matching its configuration type.
    /// Where a folder holds several files of one type the last one wins; a picker can come later.
    /// </summary>
    public void LoadFolder(string folder)
    {
        var files = Directory.EnumerateFiles(folder, "*.xml", SearchOption.TopDirectoryOnly).ToList();
        if (files.Count == 0)
        {
            Status = $"No .xml files in {folder}.";
            return;
        }

        int loaded = 0, skipped = 0;
        foreach (var file in files)
        {
            ErConfigKind kind;
            try
            {
                // Cheap streaming probe: full parsing happens once the pane is known.
                kind = ErEnvelopeReader.Read(file).Kind;
            }
            catch
            {
                skipped++;
                continue;
            }

            var pane = PaneFor(kind);
            if (pane is null) { skipped++; continue; }

            pane.Load(file);
            loaded++;
        }

        var note = AutoSelectMappingLine();

        Status = skipped == 0
            ? $"Loaded {loaded} configuration(s) from {folder}.{note}"
            : $"Loaded {loaded} configuration(s) from {folder}; skipped {skipped} unrecognised file(s).{note}";

        RunSearch();
    }

    /// <summary>
    /// Re-pools everything shared between panes whenever one of them gains or loses a file, so
    /// opening and closing one at a time behaves the same as opening a folder.
    /// </summary>
    private void OnContentChanged(ConfigPaneViewModel pane)
    {
        CollectLabels();
        CollectMappings();
    }

    /// <summary>
    /// Hands the mapping pane every mapping line embedded in the other files.
    /// <para>
    /// A format can carry its own mapping lines — one payment format does — and a model is expected
    /// to be able to as well. They are listed alongside the model mapping file's own lines, tagged
    /// with where they came from.
    /// </para>
    /// </summary>
    private void CollectMappings()
    {
        var external = new List<MappingSource>();

        foreach (var pane in new ConfigPaneViewModel[] { ModelPane, FormatPane })
        {
            var lines = pane.Configuration?.ModelMapping?.Mappings;
            if (lines is null) continue;

            var origin = $"from {pane.ExpectedKind.ToDisplayName().ToLowerInvariant()}";

            foreach (var line in lines)
                external.Add(new MappingSource(line, origin));
        }

        MappingPane.SetExternalMappings(external);
    }

    /// <summary>
    /// Pools the translations from every loaded configuration and picks a starting language.
    /// <para>
    /// Labels are not necessarily shipped with the thing they name — a model can reference ids
    /// whose translations arrive with the mapping — so all loaded files contribute.
    /// </para>
    /// </summary>
    private void CollectLabels()
    {
        Labels.Clear();

        foreach (var pane in new ConfigPaneViewModel[] { ModelPane, MappingPane, FormatPane })
            if (pane.Configuration is { } configuration)
                Labels.Add(configuration.Labels);

        Languages.Clear();
        foreach (var language in Labels.Languages) Languages.Add(language);

        OnPropertyChanged(nameof(HasLabels));

        if (Languages.Count == 0)
        {
            Labels.Language = null;
            OnPropertyChanged(nameof(SelectedLanguage));
            return;
        }

        // Prefer the machine language, then English, then whatever is there.
        var preferred = System.Globalization.CultureInfo.CurrentUICulture.Name;
        var chosen = Pick(preferred) ?? Pick("en-us") ?? Pick("en") ?? Languages[0];

        Labels.Language = chosen;
        OnPropertyChanged(nameof(SelectedLanguage));

        string? Pick(string wanted)
        {
            var exact = Languages.FirstOrDefault(l => l.Equals(wanted, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact;

            var dash = wanted.IndexOf('-');
            var root = dash > 0 ? wanted[..dash] : wanted;

            return Languages.FirstOrDefault(
                l => l.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                     l.StartsWith(root + "-", StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Points the mapping pane at the line the loaded format actually consumes.
    /// <para>
    /// The format mapping declares its model data source with a model GUID, revision and root
    /// descriptor; that triple picks one of the several mapping lines. Without this the pane opens
    /// on whichever line happens to come first in the file, which is usually the wrong one.
    /// </para>
    /// </summary>
    private string AutoSelectMappingLine()
    {
        var source = FormatPane.ModelDatasources.FirstOrDefault(d => d.ModelDescriptor is not null);

        // Lines can come from another file entirely, so this must not require the pane to have one
        // of its own — a model carrying its mapping is enough.
        if (source is null || MappingPane.Options.Count == 0) return "";

        var line = MappingPane.SelectMappingLine(
            source.ModelGuid, source.ModelRevision, source.ModelDescriptor);

        if (line is not null) return $"  Mapping line set to “{line}” to match the format.";

        var why = MappingPane.DescribeMissingLine(
            source.ModelGuid, source.ModelRevision, source.ModelDescriptor);

        return $"  No mapping line matches the format: {why}.";
    }

    private ConfigPaneViewModel? PaneFor(ErConfigKind kind) => kind switch
    {
        ErConfigKind.DataModel    => ModelPane,
        ErConfigKind.ModelMapping => MappingPane,
        ErConfigKind.Format       => FormatPane,
        _                         => null
    };

    // --------------------------------------------------------- context menu wiring

    /// <summary>
    /// Rebuilds the right-click menus for whatever was just selected. Both directions of the
    /// resolution chain are offered: a format row lists the mapping bindings it reads, and a model
    /// field lists the format rows that read it.
    /// </summary>
    /// <summary>
    /// Builds the menus for a right-clicked row without selecting it, and outlines that row so the
    /// menu is visibly attached to it. Nothing else moves: the dots, the details panel and the
    /// selection all stay where they were.
    /// </summary>
    private void OnNodeContextRequested(
        ConfigPaneViewModel pane, TreeSectionViewModel section, TreeNodeViewModel node)
    {
        SetContextRow(node);
        BuildMenus(pane, section, node);
    }

    private void SetContextRow(TreeNodeViewModel? node)
    {
        if (_contextRow is not null) _contextRow.IsContextTarget = false;

        _contextRow = node;

        if (_contextRow is not null) _contextRow.IsContextTarget = true;
    }

    private void OnNodeSelected(
        ConfigPaneViewModel pane, TreeSectionViewModel section, TreeNodeViewModel node)
    {
        // A deliberate selection supersedes whatever a menu was last opened on.
        SetContextRow(null);

        BuildMenus(pane, section, node);

        _selections[section.Marker.Key] = new Selection(pane, section, node);

        ApplyMarkers(pane, section, node);
        RefreshFilters();
    }

    private void BuildMenus(
        ConfigPaneViewModel pane, TreeSectionViewModel section, TreeNodeViewModel node)
    {
        TraceTargets.Clear();
        ConsumerTargets.Clear();
        NestedConsumerTargets.Clear();

        // Only the format component tree traces forward; only the bindings tree and the data model
        // trace back. Data source rows carry paths too, but they are not model paths.
        if (ReferenceEquals(section, FormatPane.PrimarySection))
            BuildTraceTargets(node);
        else
            BuildConsumerTargets(pane, section, node);

        OnPropertyChanged(nameof(HasTraceTargets));
        OnPropertyChanged(nameof(HasConsumerTargets));
        OnPropertyChanged(nameof(HasNestedConsumerTargets));
        OnPropertyChanged(nameof(HasAnyContextAction));
        OnPropertyChanged(nameof(ConsumerHeader));
        OnPropertyChanged(nameof(NestedConsumerHeader));
    }

    // ------------------------------------------------------------------ markers

    /// <summary>
    /// Marks every row the current selection refers to, wherever it lives, with the selecting
    /// section's dot.
    /// <para>
    /// Only that section's own dots are cleared, so marks made from elsewhere stay put and a row
    /// referenced from two directions carries two dots. Keying on the section rather than the pane
    /// matters: clicking a Format mapping row that a Format selection had just dotted must not
    /// wipe the dot that put it there.
    /// </para>
    /// </summary>
    private void ApplyMarkers(ConfigPaneViewModel pane, TreeSectionViewModel section, TreeNodeViewModel node)
    {
        var marker = section.Marker;
        ClearMarkers(marker);

        // A format row: the data sources it reads, and the mapping bindings behind them.
        if (ReferenceEquals(section, FormatPane.PrimarySection))
        {
            Mark(marker, FormatPane.FindSourceRows(node.ReferencedPaths), MarkerDirection.ReadBySelection);

            foreach (var reference in FormatPane.ReferencesFor(node))
            {
                var binding = MappingPane.FindBindingRow(reference.RootDescriptor, reference.ModelPath);
                if (binding is not null) Mark(marker, new[] { binding }, MarkerDirection.ReadBySelection);
            }

            Mark(marker, MappingRowsReading(node), MarkerDirection.ReadsSelection);
            return;
        }

        // A mapping binding: the data sources it reads, and the format rows that consume it.
        if (ReferenceEquals(section, MappingPane.SecondarySection))
        {
            Mark(marker, MappingPane.FindSourceRows(node.ReferencedPaths), MarkerDirection.ReadBySelection);
            MarkFormatComponents(marker, node);

            var (root, path) = DescribeModelTarget(pane, section, node);
            if (path is not null)
            {
                Mark(marker, FormatPane.FindConsumers(root, path).Select(c => c.Row),
                     MarkerDirection.ReadsSelection);

                Mark(marker, FormatPane.FindDatasourceRowsForModel(root, path),
                     MarkerDirection.ReadsSelection);
            }

            return;
        }

        // A model mapping data source is interesting in both directions, so mark both: the rows
        // its own formula reads, and the rows that read it. Marking only the readers left a
        // calculated field like FILTER(CustInvoiceJour.'#AttachedNotes', …) pointing at nothing,
        // because everything it depends on sits upstream.
        if (ReferenceEquals(section, MappingPane.PrimarySection))
        {
            Mark(marker, MappingPane.FindSourceRows(node.ReferencedPaths), MarkerDirection.ReadBySelection);
            MarkFormatComponents(marker, node);

            if (node.Path is { } sourcePath)
                Mark(marker, MappingPane.FindRowsReferencing(sourcePath), MarkerDirection.ReadsSelection);

            return;
        }

        // A format mapping data source: same both ways, plus the model field it stands for. Its
        // own path is a model path once the leading data source name is dropped.
        if (ReferenceEquals(section, FormatPane.SecondarySection))
        {
            Mark(marker, FormatPane.FindSourceRows(node.ReferencedPaths), MarkerDirection.ReadBySelection);

            if (node.Path is { } sourcePath)
                Mark(marker, FormatPane.FindRowsReferencing(sourcePath), MarkerDirection.ReadsSelection);

            // A format mapping row is addressed through the model data source, so its own path and
            // the paths it reads are model paths once that name is dropped — which is what makes
            // them findable among the mapping bindings.
            foreach (var path in new[] { node.Path }.Concat(node.ReferencedPaths))
            {
                if (path is null || FormatPane.ModelPathOf(path) is not { } reference) continue;

                var binding = MappingPane.FindBindingRow(reference.RootDescriptor, reference.ModelPath);
                if (binding is not null) Mark(marker, new[] { binding }, MarkerDirection.ReadBySelection);
            }

            return;
        }

    }

    /// <summary>Every tree section across the three panes.</summary>
    private IEnumerable<TreeSectionViewModel> AllSections()
    {
        foreach (var pane in new ConfigPaneViewModel[] { ModelPane, MappingPane, FormatPane })
        {
            if (pane.PrimarySection is not null) yield return pane.PrimarySection;
            if (pane.SecondarySection is not null) yield return pane.SecondarySection;
        }
    }

    /// <summary>
    /// Keeps any section showing only dotted rows in step with the marks that were just applied.
    /// Without this the filtered view would freeze on whatever was marked when it was switched on.
    /// </summary>
    private void RefreshFilters()
    {
        foreach (var section in AllSections())
            section.OnMarkersChanged();
    }

    /// <summary>
    /// Marks the format components a mapping row reads. Only meaningful for a mapping whose data
    /// source is the format itself, which is how a mapping embedded in a format is written.
    /// </summary>
    private void MarkFormatComponents(PaneMarker marker, TreeNodeViewModel node)
    {
        var prefixes = MappingPane.FormatDatasourceNames;
        if (prefixes.Count == 0) return;

        Mark(marker, FormatPane.FindComponentRows(node.ReferencedPaths, prefixes),
             MarkerDirection.ReadBySelection);
    }

    /// <summary>
    /// The mapping rows whose formulas reach a given format component — the reverse of
    /// <see cref="MarkFormatComponents"/>.
    /// <para>
    /// Guarded on the current mapping line actually having a format-backed data source, so the scan
    /// costs nothing for an ordinary model mapping, where no path could resolve to a component.
    /// </para>
    /// </summary>
    private IEnumerable<TreeNodeViewModel> MappingRowsReading(TreeNodeViewModel component)
    {
        var prefixes = MappingPane.FormatDatasourceNames;
        if (prefixes.Count == 0) yield break;

        foreach (var row in MappingPane.AllRows())
        {
            if (row.ReferencedPaths.Count == 0) continue;

            var reached = FormatPane.FindComponentRows(row.ReferencedPaths, prefixes);
            if (reached.Contains(component)) yield return row;
        }
    }

    private void Mark(PaneMarker marker, IEnumerable<TreeNodeViewModel> rows, MarkerDirection direction)
    {
        if (!_marked.TryGetValue(marker.Key, out var tracked))
            _marked[marker.Key] = tracked = new List<TreeNodeViewModel>();

        foreach (var row in rows)
        {
            row.AddMarker(marker, direction);
            row.RevealAncestors();
            tracked.Add(row);
        }
    }

    private void ClearMarkers(PaneMarker marker)
    {
        if (!_marked.TryGetValue(marker.Key, out var tracked)) return;

        foreach (var row in tracked)
            row.RemoveMarkers(marker);

        tracked.Clear();
    }

    private void BuildTraceTargets(TreeNodeViewModel node)
    {
        // The menu follows calculated fields onwards; the dots deliberately do not.
        foreach (var reference in FormatPane.TraceReferencesFor(node))
        {
            var target = reference;
            var dotted = target.ModelPath.Replace('/', '.');

            TraceTargets.Add(new ContextAction
            {
                Display = $"{target.DatasourceName}.{dotted}",
                Command = new RelayCommand(() => Status = MappingPane.LocateBinding(target))
            });
        }
    }

    private void BuildConsumerTargets(
        ConfigPaneViewModel pane, TreeSectionViewModel section, TreeNodeViewModel node)
    {
        var (rootDescriptor, modelPath) = DescribeModelTarget(pane, section, node);
        if (modelPath is null) return;

        foreach (var (row, reference) in FormatPane.FindConsumers(rootDescriptor, modelPath))
        {
            var exact = reference.ModelPath.Equals(modelPath, StringComparison.OrdinalIgnoreCase);
            var list  = exact ? ConsumerTargets : NestedConsumerTargets;

            var target = row;
            var suffix = exact ? "" : $"  ({reference.ModelPath})";

            list.Add(new ContextAction
            {
                Display = $"{row.Header}{suffix}  —  {ShortPath(row.Path)}",
                Command = new RelayCommand(() =>
                {
                    FormatPane.SelectRow(target);
                    Status = $"{modelPath} ← format row {target.Path}";
                })
            });
        }
    }

    /// <summary>
    /// Works out the model field a row stands for. A mapping binding path is already relative to
    /// its mapping line root; a data model row carries the root descriptor as its first segment.
    /// </summary>
    private (string? Root, string? Path) DescribeModelTarget(
        ConfigPaneViewModel pane, TreeSectionViewModel section, TreeNodeViewModel node)
    {
        // Bindings tree: paths are already relative to the mapping line root. A grouping row is
        // just as valid a question as a bound one — asking about a record answers for everything
        // nested under it — so this deliberately does not require a binding payload.
        if (ReferenceEquals(section, MappingPane.SecondarySection))
        {
            var root = (MappingPane.SelectedOption?.Payload as ErMappingDefinition)?.RootDescriptor;
            var isSectionRoot = ReferenceEquals(node, section.Nodes.FirstOrDefault());

            return isSectionRoot ? (null, null) : (root, node.Path);
        }

        return (null, null);
    }

    private static string ShortPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "";

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length <= 3 ? path : string.Join('/', segments.TakeLast(3));
    }

    // ------------------------------------------------------------------- search

    /// <summary>
    /// Re-applies the current search after a pane rebuilds its trees, so switching mapping line
    /// does not silently drop the highlights for a search that is still in the box.
    /// </summary>
    private void OnSectionsRebuilt(ConfigPaneViewModel pane)
    {
        ReapplyMarkersAfterRebuild(pane);

        if (!string.IsNullOrWhiteSpace(SearchText)) RunSearch();
    }

    /// <summary>
    /// A rebuild throws away the rows a pane was showing, and with them every dot cast onto it.
    /// Selections held elsewhere are still perfectly valid, so they are laid down again over the
    /// new rows; selections belonging to the rebuilt pane itself are gone and are forgotten.
    /// </summary>
    private void ReapplyMarkersAfterRebuild(ConfigPaneViewModel rebuilt)
    {
        foreach (var key in _selections.Keys.ToList())
        {
            var selection = _selections[key];
            if (!ReferenceEquals(selection.Pane, rebuilt)) continue;

            // Its rows no longer exist, so neither the selection nor its marks mean anything.
            _selections.Remove(key);
            ClearMarkers(selection.Section.Marker);
        }

        foreach (var selection in _selections.Values.ToList())
            ApplyMarkers(selection.Pane, selection.Section, selection.Node);

        RefreshFilters();
    }

    private void RunSearch()
    {
        var model   = ModelPane.ApplySearch(SearchText, ExactMatch);
        var mapping = MappingPane.ApplySearch(SearchText, ExactMatch);
        var format  = FormatPane.ApplySearch(SearchText, ExactMatch);

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            SearchFailed = false;
            return;
        }

        var total = model + mapping + format;
        SearchFailed = total == 0;

        var note = ModelPane.SearchNote is null ? "" : $"  ({ModelPane.SearchNote})";

        Status = total == 0
            ? $"No match for “{SearchText}”{(ExactMatch ? " (exact)" : "")}."
            : $"{total} match(es) — model {model}, mapping {mapping}, format {format}.{note}";
    }
}
