using System.Text.RegularExpressions;

namespace D365ERAnalyzer.ViewModels;

public static class TextUtil
{
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Flattens an expression to a single line for display in a tree row. Expressions are stored
    /// with real newlines (encoded as &#xA; in the XML), so they need collapsing before they can
    /// sit next to a node caption.
    /// </summary>
    public static string? OneLine(string? text, int max = 140)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var flat = Whitespace.Replace(text, " ").Trim();
        return flat.Length <= max ? flat : flat[..max] + "…";
    }

    public static string? Join(params string?[] parts)
    {
        var text = string.Join("  ·  ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        return text.Length == 0 ? null : text;
    }
}
