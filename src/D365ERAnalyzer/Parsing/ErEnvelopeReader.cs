using System.Xml;
using D365ERAnalyzer.Model;

namespace D365ERAnalyzer.Parsing;

/// <summary>
/// Streams an exported ER configuration and extracts its envelope plus a shallow list of the
/// versioned objects it contains. Uses <see cref="XmlReader"/> rather than a DOM: the model
/// mapping sample is 3.4 MB and there is no reason to materialise it for a header read.
/// </summary>
public static class ErEnvelopeReader
{
    public static ErEnvelope Read(string path)
    {
        string? solName = null, solDesc = null, baseName = null, baseRef = null, countries = null, vendor = null;
        string? pubVersion = null, desc = null, timestamp = null, status = null;
        int? versionNumber = null;
        var kind = ErConfigKind.Unknown;
        var objects = new List<ErContainedObject>();

        var settings = new XmlReaderSettings
        {
            IgnoreComments = true,
            IgnoreWhitespace = true,
            DtdProcessing = DtdProcessing.Prohibit
        };

        using var reader = XmlReader.Create(path, settings);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element) continue;

            switch (reader.Name)
            {
                case "ERSolutionVersion":
                    versionNumber = ParseInt(reader.GetAttribute("Number"));
                    pubVersion    = reader.GetAttribute("PublicVersionNumber");
                    desc          = reader.GetAttribute("Description");
                    timestamp     = reader.GetAttribute("DateTime");
                    status        = reader.GetAttribute("VersionStatus");
                    break;

                case "ERSolution":
                    solName   = reader.GetAttribute("Name");
                    solDesc   = reader.GetAttribute("Description");
                    // "BaseName.o." is the cached display name of the base configuration.
                    baseName  = reader.GetAttribute("BaseName.o.");
                    baseRef   = reader.GetAttribute("Base");
                    countries = reader.GetAttribute("CountryRegionCodes");
                    break;

                case "ERVendor":
                    vendor ??= reader.GetAttribute("Name");
                    break;

                // ---- the elements that identify the configuration type ----

                case "ERDataModel":
                    kind = ErConfigKind.DataModel;
                    objects.Add(new ErContainedObject
                    {
                        Kind        = "Data model",
                        Name        = reader.GetAttribute("Name") ?? "(unnamed)",
                        Description = reader.GetAttribute("Description"),
                        Id          = reader.GetAttribute("ID."),
                        Detail      = FormatDetail(("default root", reader.GetAttribute("Root")))
                    });
                    break;

                case "ERModelMapping":
                    kind = ErConfigKind.ModelMapping;
                    objects.Add(new ErContainedObject
                    {
                        Kind        = "Mapping line",
                        Name        = reader.GetAttribute("Name") ?? "(unnamed)",
                        Description = reader.GetAttribute("Description"),
                        Id          = reader.GetAttribute("ID."),
                        // The triple that a format's datasource matches against (§6 of the schema doc).
                        Detail      = FormatDetail(
                            ("root", reader.GetAttribute("DataContainerDescriptor")),
                            ("model", reader.GetAttribute("ModelName")),
                            ("model version", VersionOf(reader.GetAttribute("ModelVersion"))))
                    });
                    break;

                case "ERTextFormat":
                    kind = ErConfigKind.Format;
                    objects.Add(new ErContainedObject
                    {
                        Kind        = "Format",
                        Name        = reader.GetAttribute("Name") ?? "(unnamed)",
                        Description = reader.GetAttribute("Description"),
                        Id          = reader.GetAttribute("ID.")
                    });
                    break;

                case "ERFormatMapping":
                    // A format export always carries its format mapping alongside the format itself.
                    if (kind == ErConfigKind.Unknown) kind = ErConfigKind.Format;
                    objects.Add(new ErContainedObject
                    {
                        Kind        = "Format mapping",
                        Name        = reader.GetAttribute("Name") ?? "(unnamed)",
                        Description = reader.GetAttribute("Description"),
                        Id          = reader.GetAttribute("ID."),
                        Detail      = FormatDetail(
                            ("format version", VersionOf(reader.GetAttribute("FormatVersion"))))
                    });
                    break;
            }
        }

        var envelope = new ErEnvelope
        {
            FilePath            = path,
            Kind                = kind,
            SolutionName        = solName,
            SolutionDescription = solDesc,
            BaseName            = baseName,
            BaseReference       = baseRef,
            CountryRegionCodes  = countries,
            VendorName          = vendor,
            VersionNumber       = versionNumber,
            PublicVersionNumber = pubVersion,
            Description         = desc,
            Timestamp           = timestamp,
            VersionStatus       = status
        };
        envelope.Objects.AddRange(objects);
        return envelope;
    }

    /// <summary>Pulls the version off a "{guid},7" reference, returning "7".</summary>
    private static string? VersionOf(string? reference)
    {
        if (string.IsNullOrEmpty(reference)) return null;
        var comma = reference.LastIndexOf(',');
        return comma >= 0 && comma < reference.Length - 1 ? reference[(comma + 1)..] : null;
    }

    private static int? ParseInt(string? s) => int.TryParse(s, out var v) ? v : null;

    private static string? FormatDetail(params (string Label, string? Value)[] parts)
    {
        var kept = parts.Where(p => !string.IsNullOrEmpty(p.Value))
                        .Select(p => $"{p.Label}: {p.Value}");
        var text = string.Join("  \u00b7  ", kept);
        return text.Length == 0 ? null : text;
    }
}
