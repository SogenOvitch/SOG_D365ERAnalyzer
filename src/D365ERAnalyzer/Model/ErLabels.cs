using System.Collections.Generic;

namespace D365ERAnalyzer.Model;

/// <summary>
/// The label translations a configuration can embed, keyed by label id and language.
/// <para>
/// Labels are referenced from <c>@Label</c> and <c>@Description</c> as <c>@GER_LABEL:Accepted</c>
/// or <c>@SYS28013</c>; the part after the colon (or after <c>@</c>) is the id. Not every export
/// carries them — of the samples, a payment model holds 607 ids across 65 languages while an
/// invoice trio holds none at all — so resolution always has to survive a miss.
/// </para>
/// </summary>
public sealed class ErLabels
{
    private readonly Dictionary<string, Dictionary<string, string>> _byId =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly SortedSet<string> _languages =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Language ids present, e.g. "en-us", "fr". Casing in the files is inconsistent.</summary>
    public IReadOnlyCollection<string> Languages => _languages;

    public int Count { get; private set; }

    public bool IsEmpty => Count == 0;

    public void Add(string labelId, string languageId, string value)
    {
        if (!_byId.TryGetValue(labelId, out var byLanguage))
            _byId[labelId] = byLanguage = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        byLanguage[languageId] = value;
        _languages.Add(languageId);
        Count++;
    }

    /// <summary>
    /// Resolves a raw <c>@Label</c> value for a language, returning null when it is not a label
    /// reference or the translation is missing.
    /// </summary>
    public string? Resolve(string? raw, string? languageId)
    {
        var id = ExtractId(raw);
        if (id is null || languageId is null) return null;
        if (!_byId.TryGetValue(id, out var byLanguage)) return null;

        if (byLanguage.TryGetValue(languageId, out var exact)) return exact;

        // "fr" should still answer for "fr-BE" and the other way round.
        var dash = languageId.IndexOf('-');
        var root = dash > 0 ? languageId[..dash] : languageId;

        if (byLanguage.TryGetValue(root, out var rooted)) return rooted;

        foreach (var (candidate, value) in byLanguage)
            if (candidate.StartsWith(root + "-", StringComparison.OrdinalIgnoreCase))
                return value;

        return null;
    }

    /// <summary>
    /// The id inside a label reference: "@GER_LABEL:Accepted" gives "Accepted", "@SYS28013" gives
    /// "SYS28013". Returns null for plain text.
    /// </summary>
    public static string? ExtractId(string? raw)
    {
        if (string.IsNullOrEmpty(raw) || raw[0] != '@') return null;

        var colon = raw.IndexOf(':');
        var id = colon >= 0 && colon < raw.Length - 1 ? raw[(colon + 1)..] : raw[1..];

        return id.Length == 0 ? null : id;
    }
}
