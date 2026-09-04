namespace D365ERAnalyzer.ViewModels;

/// <summary>
/// Row ordering for the trees: names beginning with <c>$</c> or <c>#</c> first, everything else
/// alphabetically after them.
/// <para>
/// Those prefixes mark calculated fields and other rows somebody added by hand, which are what a
/// reader is usually looking for; the fields that came with the model or the table are the
/// background. Ordering by them keeps the interesting rows together at the top of each level
/// instead of scattered through it.
/// </para>
/// </summary>
public static class TreeSort
{
    public static readonly IComparer<string> ByName = Comparer<string>.Create(Compare);

    public static IOrderedEnumerable<T> Sorted<T>(IEnumerable<T> items, Func<T, string> name) =>
        items.OrderBy(name, ByName);

    private static int Compare(string? left, string? right)
    {
        var byGroup = Group(left).CompareTo(Group(right));

        return byGroup != 0
            ? byGroup
            : string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static int Group(string? name) =>
        !string.IsNullOrEmpty(name) && (name[0] == '$' || name[0] == '#') ? 0 : 1;
}
