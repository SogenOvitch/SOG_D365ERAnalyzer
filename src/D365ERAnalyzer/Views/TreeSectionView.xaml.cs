using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using D365ERAnalyzer.ViewModels;

namespace D365ERAnalyzer.Views;

public partial class TreeSectionView : UserControl
{
    private bool _bringingIntoView;
    private TreeSectionViewModel? _section;

    public TreeSectionView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_section is not null) _section.BringIntoView -= OnBringIntoViewRequested;

        _section = DataContext as TreeSectionViewModel;

        if (_section is not null) _section.BringIntoView += OnBringIntoViewRequested;
    }

    /// <summary>
    /// TreeView.SelectedItem is read-only, so selection is pushed to the view model here. The pane
    /// listens for it to fill the details fields, and the expand/collapse commands need it to know
    /// which subtree to act on.
    /// </summary>
    private void OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is TreeSectionViewModel section)
            section.SelectedNode = e.NewValue as TreeNodeViewModel;
    }

    /// <summary>
    /// Right-clicking focuses the row under the cursor before the menu opens, so the menu is built
    /// for that row.
    /// <para>
    /// Setting IsSelected is not enough: the row may already be selected in this section while the
    /// menus reflect a row picked in another section or pane, in which case nothing would change
    /// and the menu would describe the wrong row. Re-announcing covers both cases.
    /// </para>
    /// </summary>
    private void OnRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsInnermost(sender, e)) return;
        if (sender is not TreeViewItem item) return;
        if (item.DataContext is not TreeNodeViewModel node) return;
        if (DataContext is not TreeSectionViewModel section) return;

        item.IsSelected = true;
        section.Reselect(node);
    }

    /// <summary>
    /// Clicking a row that is already selected here raises no SelectedItemChanged, so the pane
    /// details would keep showing whatever was picked in the other section of a split pane. Tell
    /// the section anyway.
    /// </summary>
    private void OnLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsInnermost(sender, e)) return;

        if (sender is not TreeViewItem { IsSelected: true } item) return;
        if (item.DataContext is not TreeNodeViewModel node) return;
        if (DataContext is not TreeSectionViewModel section) return;

        section.Reselect(node);
    }

    /// <summary>
    /// True when the handler is running for the deepest row under the pointer. Mouse events bubble
    /// from a child row through every ancestor row, so without this a click on a leaf would also be
    /// handled by each of its parents.
    /// </summary>
    private static bool IsInnermost(object sender, RoutedEventArgs e)
    {
        for (var source = e.OriginalSource as DependencyObject;
             source is not null;
             source = VisualTreeHelper.GetParent(source))
        {
            if (source is TreeViewItem item) return ReferenceEquals(item, sender);
        }

        return false;
    }

    /// <summary>
    /// Keeps selection from scrolling the tree sideways.
    /// <para>
    /// A TreeViewItem asks to be brought fully into view, which on a deep row drags the horizontal
    /// scrollbar across. Re-issuing the request with a zero-width rectangle keeps the vertical
    /// scroll — still needed when jumping between panes — and drops the horizontal part. The guard
    /// is required because the inner call raises this same event again.
    /// </para>
    /// </summary>
    private void OnRequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (_bringingIntoView || sender is not TreeViewItem item) return;

        e.Handled = true;
        _bringingIntoView = true;
        try
        {
            item.BringIntoView(new Rect(0, 0, 0, item.ActualHeight));
        }
        finally
        {
            _bringingIntoView = false;
        }
    }

    /// <summary>
    /// Scrolls to a row selected from code — a jump between panes, or a pick from a right-click
    /// menu.
    /// <para>
    /// The containers along the way only exist once the branches above have been expanded and laid
    /// out, and virtualisation defers that past the current dispatcher frame. So this tries once
    /// after layout settles and, if the row still has no container, once more at a lower priority
    /// rather than silently doing nothing.
    /// </para>
    /// </summary>
    private void OnBringIntoViewRequested(TreeNodeViewModel node)
    {
        Attempt(DispatcherPriority.Background, retry: true);

        void Attempt(DispatcherPriority priority, bool retry)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                Tree.UpdateLayout();

                var container = FindContainer(Tree, node);
                if (container is not null)
                {
                    ScrollTo(container);
                    return;
                }

                if (retry) Attempt(DispatcherPriority.ContextIdle, retry: false);
            }), priority);
        }
    }

    /// <summary>
    /// Puts a row about a third of the way down the viewport.
    /// <para>
    /// BringIntoView only guarantees the row is somewhere on screen, and under virtualisation the
    /// scroll extent is an estimate that shifts as rows are realised — which is what makes the
    /// thumb grow and shrink while scrolling, and what made a jump land short. Scrolling the
    /// viewer to a measured offset instead is exact, and the TreeView is set to pixel scroll units
    /// so the offset means pixels rather than item counts.
    /// </para>
    /// </summary>
    private void ScrollTo(TreeViewItem container)
    {
        var viewer = FindScrollViewer(Tree);
        if (viewer is null)
        {
            container.BringIntoView(new Rect(0, 0, 0, container.ActualHeight));
            return;
        }

        var offset = container.TransformToAncestor(viewer).Transform(new Point(0, 0)).Y;
        var target = viewer.VerticalOffset + offset - viewer.ViewportHeight / 3;

        viewer.ScrollToVerticalOffset(Math.Max(0, target));
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer viewer) return viewer;

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (found is not null) return found;
        }

        return null;
    }

    private static TreeViewItem? FindContainer(ItemsControl parent, TreeNodeViewModel node)
    {
        for (var i = 0; i < parent.Items.Count; i++)
        {
            if (parent.ItemContainerGenerator.ContainerFromIndex(i) is not TreeViewItem container)
                continue;

            if (ReferenceEquals(container.DataContext, node)) return container;

            if (!container.IsExpanded) continue;

            container.UpdateLayout();
            var found = FindContainer(container, node);
            if (found is not null) return found;
        }

        return null;
    }
}
