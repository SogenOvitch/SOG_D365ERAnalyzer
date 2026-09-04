using System.Xml.Linq;

namespace D365ERAnalyzer.Parsing;

/// <summary>
/// Pulls data source paths out of a serialized expression.
/// <para>
/// ER writes every formula twice: as <c>@ExpressionAsString</c> for display, and as a typed AST of
/// <c>ERExpression*</c> elements. The AST carries paths in plain attributes, so extraction is an
/// attribute sweep — no tokenizer. See docs/er-xml-schema.md §5.
/// </para>
/// </summary>
public static class ExpressionPaths
{
    private static readonly string[] PathAttributes =
    {
        "ItemPath",     // ERExpression*ItemValue, ERExpressionGenericCall
        "FieldPath",    // ERModelGroupByAggregation, ERModelGroupByFieldReference
        "ListToGroup"   // ERModelGroupByFunction
    };

    /// <summary>Distinct paths referenced anywhere under <paramref name="root"/>, in document order.</summary>
    public static List<string> Extract(XElement? root)
    {
        var found = new List<string>();
        if (root is null) return found;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Walk(root, found, seen);
        return found;
    }

    private static void Walk(XElement element, List<string> found, HashSet<string> seen)
    {
        // A path interrupted by a function call continues in @RelativePath, measured from whatever
        // the DataContainer evaluates to:
        //
        //   FIRSTORNULL(Tables.X.'>Relations'.Y).'$PostalAddress'.'$Country'.ISOcode
        //
        // serialises the FIRSTORNULL part as the container and the rest as the relative path. Those
        // trailing segments are exactly the quoted $ and # identifiers, so skipping them as "merely
        // relative" loses the very nodes a reader is looking for. Rejoining the two halves gives
        // the absolute path the rest of the tool matches on.
        if (element.Name.LocalName == "ERExpressionGenericRelativeItemValue")
        {
            var relative = element.Attribute("RelativePath")?.Value;
            var container = ContainerPath(element.Element("DataContainer"));

            if (!string.IsNullOrEmpty(relative) && !string.IsNullOrEmpty(container))
                Add(found, seen, $"{container}/{relative}");
        }

        foreach (var name in PathAttributes)
        {
            var value = element.Attribute(name)?.Value;
            if (!string.IsNullOrEmpty(value)) Add(found, seen, value);
        }

        foreach (var child in element.Elements())
            Walk(child, found, seen);
    }

    /// <summary>
    /// The absolute path a DataContainer evaluates to. Relative items nest, so a container can
    /// itself be one, and its path is resolved the same way.
    /// </summary>
    private static string? ContainerPath(XElement? container)
    {
        if (container is null) return null;

        foreach (var element in container.DescendantsAndSelf())
        {
            if (element.Name.LocalName == "ERExpressionGenericRelativeItemValue")
            {
                var relative = element.Attribute("RelativePath")?.Value;
                var inner = ContainerPath(element.Element("DataContainer"));

                if (!string.IsNullOrEmpty(relative) && !string.IsNullOrEmpty(inner))
                    return $"{inner}/{relative}";
            }

            var path = element.Attribute("ItemPath")?.Value;
            if (!string.IsNullOrEmpty(path)) return path;
        }

        return null;
    }

    private static void Add(List<string> found, HashSet<string> seen, string path)
    {
        if (seen.Add(path)) found.Add(path);
    }

    /// <summary>
    /// The paths an expression <i>evaluates to</i>, as opposed to every path it mentions.
    /// <para>
    /// <see cref="Extract"/> answers "what does this formula read", which is what the dots want.
    /// Rewriting a path through a calculated data source needs a narrower answer: appending
    /// "/Amount" to the list a FILTER walks is right, appending it to the condition that FILTER
    /// tests is nonsense. Only the value-producing branch of each operator is followed.
    /// </para>
    /// </summary>
    public static List<string> ExtractResult(XElement? source)
    {
        var found = new List<string>();
        if (source is null) return found;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Result(source))
            if (seen.Add(path)) found.Add(path);

        return found;
    }

    private static IEnumerable<string> Result(XElement element)
    {
        var name = element.Name.LocalName;

        // A plain reference is its own result.
        if (name.EndsWith("ItemValue", StringComparison.Ordinal) &&
            element.Attribute("ItemPath")?.Value is { Length: > 0 } itemPath &&
            name != "ERExpressionGenericRelativeItemValue")
        {
            yield return itemPath;
            yield break;
        }

        if (name == "ERExpressionGenericRelativeItemValue")
        {
            var relative = element.Attribute("RelativePath")?.Value;
            var container = ContainerPath(element.Element("DataContainer"));

            if (!string.IsNullOrEmpty(relative) && !string.IsNullOrEmpty(container))
                yield return $"{container}/{relative}";

            yield break;
        }

        // Both branches of a conditional can be the answer.
        if (name == "ERExpressionGenericIf")
        {
            foreach (var branch in new[] { "TrueValue", "FalseValue" })
            foreach (var child in element.Element(branch)?.Elements() ?? Enumerable.Empty<XElement>())
            foreach (var path in Result(child))
                yield return path;

            yield break;
        }

        // A wrapper that carries its operand through: the list a filter walks, the expression an
        // adapter re-types, the formula a data source holds.
        var passthrough = element.Element("List")
                       ?? element.Element("Expression")
                       ?? (name.EndsWith("Adapter", StringComparison.Ordinal) ? element.Elements().FirstOrDefault() : null);

        if (passthrough is null) yield break;

        foreach (var child in passthrough.Name.LocalName is "List" or "Expression"
                            ? passthrough.Elements()
                            : new[] { passthrough })
        foreach (var path in Result(child))
            yield return path;
    }
}
