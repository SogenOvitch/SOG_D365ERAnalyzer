using System.Collections.Generic;

namespace D365ERAnalyzer.Model;

/// <summary>
/// The <c>ERSolutionVersion</c> / <c>ERSolution</c> header of an exported ER configuration,
/// plus a shallow listing of the versioned objects it contains.
/// <para>
/// M1 reads only this much. The full descriptor / datasource / format trees arrive in M2.
/// </para>
/// </summary>
public sealed class ErEnvelope
{
    public required string FilePath { get; init; }
    public required ErConfigKind Kind { get; init; }

    // ERSolution
    public string? SolutionName { get; init; }
    public string? SolutionDescription { get; init; }
    public string? BaseName { get; init; }
    public string? BaseReference { get; init; }
    public string? CountryRegionCodes { get; init; }
    public string? VendorName { get; init; }

    // ERSolutionVersion
    public int? VersionNumber { get; init; }
    public string? PublicVersionNumber { get; init; }
    public string? Description { get; init; }
    public string? Timestamp { get; init; }
    public string? VersionStatus { get; init; }

    public List<ErContainedObject> Objects { get; } = new();

    /// <summary>Root tree node caption: "CEZ Invoice model (322.7)".</summary>
    public string RootCaption =>
        string.IsNullOrEmpty(PublicVersionNumber)
            ? SolutionName ?? System.IO.Path.GetFileNameWithoutExtension(FilePath)
            : $"{SolutionName} ({PublicVersionNumber})";
}

/// <summary>One versioned object inside a configuration — a data model, a mapping line, a format.</summary>
public sealed class ErContainedObject
{
    public required string Kind { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }

    /// <summary>Join-relevant detail, e.g. "root: InvoiceCustomer · model v7".</summary>
    public string? Detail { get; init; }

    public string? Id { get; init; }
}
