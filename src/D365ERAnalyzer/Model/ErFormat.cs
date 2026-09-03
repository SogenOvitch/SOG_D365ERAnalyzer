using System.Collections.Generic;

namespace D365ERAnalyzer.Model;

/// <summary>
/// A format configuration. Carries two versioned objects: the format component tree and the
/// format mapping that binds it. The component tree holds no bindings itself — they live in
/// <see cref="ErFormatMappingInfo.BindingsByComponent"/>, keyed by component GUID.
/// </summary>
public sealed class ErFormat
{
    public string? Name { get; init; }
    public string? Id { get; init; }
    public string? Description { get; init; }

    public ErFormatComponent? Root { get; set; }
    public ErFormatMappingInfo? Mapping { get; set; }
}

public sealed class ErFormatComponent
{
    /// <summary>Element name with the "ERTextFormat" prefix stripped, e.g. "XMLElement".</summary>
    public required string Kind { get; init; }

    public Guid? Id { get; init; }
    public string? Name { get; init; }
    public string? Value { get; init; }
    public string? DateFormat { get; init; }
    public string? Encoding { get; init; }
    public string? Multiplicity { get; init; }
    public string? Transformation { get; init; }

    // ---- Excel and container components ----
    // Excel components are not named the way XML ones are: a cell is identified by its range,
    // a sheet by its sheet name, and 255 of the 315 cells in the samples carry no @Name at all.

    /// <summary>Cell or range address, e.g. "A1" or "B12:C14".</summary>
    public string? ExcelRange { get; init; }

    public string? ExcelSheetName { get; init; }
    public string? ReplicationDirection { get; init; }

    /// <summary>Delimiter of a Sequence component.</summary>
    public string? Delimiter { get; init; }

    public string? MaximalLength { get; init; }

    /// <summary>Data type of a DataItem component.</summary>
    public string? DataType { get; init; }

    public List<ErFormatComponent> Children { get; } = new();
}

public sealed class ErFormatMappingInfo
{
    public string? Name { get; init; }
    public string? Id { get; init; }
    public string? FormatVersion { get; init; }

    public List<ErDatasourceNode> Datasources { get; } = new();

    /// <summary>Component GUID → its property bindings. One component can carry several.</summary>
    public Dictionary<Guid, List<ErComponentBinding>> BindingsByComponent { get; } = new();

    public int BindingCount { get; set; }
}

/// <summary>
/// One <c>ERFormatComponentPropertyBinding</c>.
/// <para>
/// <c>@PropertyName</c> is absent on the binding that supplies the component's own value — those
/// are the interesting ones (170 of 792 in the sample). The named ones are <c>Enabled</c> (620)
/// and <c>FileName</c> (2).
/// </para>
/// </summary>
public sealed class ErComponentBinding
{
    public Guid Component { get; init; }

    /// <summary>Null for a value binding; otherwise "Enabled" or "FileName".</summary>
    public string? PropertyName { get; init; }

    public string? Expression { get; init; }
    public List<string> ReferencedPaths { get; } = new();

    /// <summary>The binding that supplies the component's value.</summary>
    public bool IsValueBinding => string.IsNullOrEmpty(PropertyName);

    public bool IsEnabledBinding =>
        "Enabled".Equals(PropertyName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// An <c>Enabled</c> binding hard-wired to <c>false</c> — the component is switched off.
    /// 579 of the sample's 989 components are, which is normal for a config derived from a broad
    /// base format: the derivation turns off the elements it does not emit. Never treat these as
    /// noise; a disabled component is exactly what someone reading the tree needs to see.
    /// </summary>
    public bool IsDisabling =>
        IsEnabledBinding && "false".Equals(Expression?.Trim(), StringComparison.OrdinalIgnoreCase);
}
