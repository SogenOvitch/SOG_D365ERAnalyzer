namespace D365ERAnalyzer.Model;

/// <summary>
/// <c>ERDataContainerDescriptorItem/@Type</c>. Values decoded empirically from the samples —
/// see docs/er-xml-schema.md §2. 2 and 12 were never observed.
/// </summary>
public enum ErDataType
{
    Unknown   = 0,
    Boolean   = 1,
    Int64     = 3,
    Integer   = 4,
    Real      = 5,
    String    = 6,
    Date      = 7,
    Time      = 8,
    Enum      = 9,
    Record    = 10,
    List      = 11,
    Container = 13,
    DateTime  = 14
}

public static class ErDataTypeExtensions
{
    public static string ToDisplayName(this ErDataType type) => type switch
    {
        ErDataType.Boolean   => "boolean",
        ErDataType.Int64     => "int64",
        ErDataType.Integer   => "integer",
        ErDataType.Real      => "real",
        ErDataType.String    => "string",
        ErDataType.Date      => "date",
        ErDataType.Time      => "time",
        ErDataType.Enum      => "enum",
        ErDataType.Record    => "record",
        ErDataType.List      => "list",
        ErDataType.Container => "container",
        ErDataType.DateTime  => "datetime",
        _                    => "?"
    };

    /// <summary>Types whose items expand into child nodes: records, lists, and enums (their values).</summary>
    public static bool IsExpandable(this ErDataType type) =>
        type is ErDataType.Record or ErDataType.List or ErDataType.Enum;
}
