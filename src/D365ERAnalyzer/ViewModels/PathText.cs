namespace D365ERAnalyzer.ViewModels;

/// <summary>
/// Puts a path into the one shape everything else compares against.
/// <para>
/// The same path is written two ways depending on where it was found: the expression trees use
/// slashes, the display strings use dots. Comparing them means settling on one, and slashes win
/// because that is what the model tree already stores.
/// </para>
/// </summary>
public static class PathText
{
    /// <summary>Slash-separated form of a path, with any dot separators converted.</summary>
    public static string Normalise(string path) => path.Replace('.', '/').Trim('/');

    public static string[] Segments(string path) =>
        Normalise(path).Split('/', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>True when <paramref name="path"/> ends with the segments of <paramref name="suffix"/>.</summary>
    public static bool EndsWithSegments(string path, string suffix)
    {
        var full = Segments(path);
        var wanted = Segments(suffix);

        if (wanted.Length == 0 || full.Length < wanted.Length) return false;

        var offset = full.Length - wanted.Length;

        for (var i = 0; i < wanted.Length; i++)
            if (!full[offset + i].Equals(wanted[i], StringComparison.OrdinalIgnoreCase))
                return false;

        return true;
    }

    /// <summary>Drops a leading data source name, e.g. "model.Payments.Alias" → "Payments/Alias".</summary>
    public static string? StripPrefix(string path, string prefix)
    {
        var segments = Segments(path);
        if (segments.Length < 2) return null;

        return segments[0].Equals(prefix, StringComparison.OrdinalIgnoreCase)
            ? string.Join('/', segments.Skip(1))
            : null;
    }

    /// <summary>True for the calculated data sources written with a $ or # prefix.</summary>
    public static bool IsCalculatedName(string? name) =>
        !string.IsNullOrEmpty(name) && (name[0] == '$' || name[0] == '#');
}
