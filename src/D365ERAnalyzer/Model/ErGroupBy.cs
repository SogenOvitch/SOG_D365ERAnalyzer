using System.Collections.Generic;

namespace D365ERAnalyzer.Model;

/// <summary>
/// An <c>ERModelGroupByFunction</c> data source: a list, the fields it is grouped by, and the
/// aggregations computed over each group.
/// </summary>
public sealed class ErGroupBySpec
{
    public string? ListToGroup { get; init; }
    public bool SourceListIsAlreadySorted { get; init; }

    /// <summary>Field paths the list is grouped by.</summary>
    public List<string> GroupedFields { get; } = new();

    public List<ErAggregation> Aggregations { get; } = new();
}

public sealed class ErAggregation
{
    /// <summary>Absent when the designer left the generated name in place.</summary>
    public string? Name { get; init; }

    public required string FieldPath { get; init; }

    public ErAggregationKind Kind { get; init; }

    /// <summary>Raw <c>@SelectionField</c>, kept so an unrecognised value can still be shown.</summary>
    public int RawKind { get; init; }
}

/// <summary>
/// <c>ERModelGroupByAggregation/@SelectionField</c>. Decoded from the samples by correlating the
/// value with the generated aggregation names: 19 <c>_Sum</c> names carry 1, one <c>_Max</c>
/// carries 3, and 26 <c>_Count</c> names carry 4.
/// </summary>
public enum ErAggregationKind
{
    Unknown = 0,
    Sum     = 1,

    /// <summary>
    /// Inferred, not confirmed: the single occurrence has no name to corroborate it. Min is what
    /// fits between Sum and Max in the observed ordering.
    /// </summary>
    Min     = 2,

    Max     = 3,
    Count   = 4
}

public static class ErAggregationKindExtensions
{
    public static string ToDisplayName(this ErAggregationKind kind) => kind switch
    {
        ErAggregationKind.Sum   => "sum",
        ErAggregationKind.Min   => "min",
        ErAggregationKind.Max   => "max",
        ErAggregationKind.Count => "count",
        _                       => "?"
    };
}
