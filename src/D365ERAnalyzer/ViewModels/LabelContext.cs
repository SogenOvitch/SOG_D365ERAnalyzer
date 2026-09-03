using D365ERAnalyzer.Model;

namespace D365ERAnalyzer.ViewModels;

/// <summary>
/// Turns label references into readable text for the chosen language.
/// <para>
/// Every loaded configuration contributes its own translations, because a label used in one file
/// is often defined in another: a data model can name a field <c>@GER_LABEL:Accepted</c> while the
/// translations for it ship with the mapping. Resolution therefore runs across all of them.
/// </para>
/// </summary>
public sealed class LabelContext
{
    private readonly List<ErLabels> _sources = new();

    /// <summary>Language used for display, e.g. "fr" or "en-us".</summary>
    public string? Language { get; set; }

    public IReadOnlyList<string> Languages =>
        _sources.SelectMany(s => s.Languages)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
                .ToList();

    public bool HasLabels => _sources.Any(s => !s.IsEmpty);

    public int Count => _sources.Sum(s => s.Count);

    public void Clear() => _sources.Clear();

    public void Add(ErLabels labels)
    {
        if (!labels.IsEmpty) _sources.Add(labels);
    }

    /// <summary>
    /// The readable form of a label reference, or the value unchanged when it is plain text or no
    /// translation is available. Never returns null for a non-null input: an unresolved id is more
    /// useful on screen than a blank.
    /// </summary>
    public string? Display(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;

        foreach (var source in _sources)
            if (source.Resolve(raw, Language) is { } resolved)
                return resolved;

        return raw;
    }
}
