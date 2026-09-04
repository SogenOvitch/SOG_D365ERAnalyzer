using System.IO;
using System.Xml.Linq;
using D365ERAnalyzer.Model;

namespace D365ERAnalyzer.Parsing;

/// <summary>
/// Full parse of an exported ER configuration into the domain model.
/// <para>
/// Uses <see cref="XDocument"/> rather than streaming: the trees are nested and cross-referenced,
/// and even the 3.4 MB model mapping loads comfortably. The envelope still comes from the
/// streaming <see cref="ErEnvelopeReader"/> so both paths share one implementation.
/// </para>
/// </summary>
public static class ErConfigurationReader
{
    public static ErConfiguration Read(string path)
    {
        var envelope = ErEnvelopeReader.Read(path);
        var root = XDocument.Load(path).Root
                   ?? throw new InvalidDataException("Empty document.");

        return new ErConfiguration
        {
            Envelope     = envelope,
            Labels       = ReadLabels(root),
            DataModel    = envelope.Kind == ErConfigKind.DataModel ? ReadDataModel(root) : null,

            // Read unconditionally: mapping lines are not confined to model mapping files. A
            // format can embed its own, and the same is expected of models, so the reader looks
            // everywhere and lets the shell decide what to do with what it finds.
            ModelMapping = ReadModelMapping(root),
            Format       = envelope.Kind == ErConfigKind.Format       ? ReadFormat(root)       : null
        };
    }

    /// <summary>
    /// Reads the embedded label translations. They sit in their own section rather than beside the
    /// things they name, and an export may carry none at all.
    /// </summary>
    private static ErLabels ReadLabels(XElement root)
    {
        var labels = new ErLabels();

        foreach (var element in root.Descendants("ERLabel"))
        {
            var id = element.Attribute("LabelId")?.Value;
            var language = element.Attribute("LanguageId")?.Value;
            var value = element.Attribute("LabelValue")?.Value;

            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(language) || value is null) continue;

            labels.Add(id, language, value);
        }

        return labels;
    }

    // ---------------------------------------------------------------- data model

    private static ErDataModel? ReadDataModel(XElement root)
    {
        // Only the descriptors under Model/ERDataModel — the Delta carries its own copies.
        var modelElement = root.Descendants("ERDataModel").FirstOrDefault();
        if (modelElement is null) return null;

        var model = new ErDataModel
        {
            Name        = modelElement.Attribute("Name")?.Value,
            Id          = modelElement.Attribute("ID.")?.Value,
            Description = modelElement.Attribute("Description")?.Value,
            DefaultRoot = modelElement.Attribute("Root")?.Value
        };

        foreach (var element in Contents(modelElement).Elements("ERDataContainerDescriptor"))
        {
            var name = element.Attribute("Name")?.Value ?? element.Attribute("ID.")?.Value;
            if (string.IsNullOrEmpty(name)) continue;

            var descriptor = new ErDescriptor
            {
                Name        = name,
                Id          = element.Attribute("ID.")?.Value,
                Label       = element.Attribute("Label")?.Value,
                Description = element.Attribute("Description")?.Value,
                IsRoot      = element.Attribute("IsRoot")?.Value == "1",
                IsEnum      = element.Attribute("IsEnum")?.Value == "1"
            };

            foreach (var item in Contents(element).Elements("ERDataContainerDescriptorItem"))
            {
                var itemName = item.Attribute("Name")?.Value;
                if (string.IsNullOrEmpty(itemName)) continue;

                var rawType = int.TryParse(item.Attribute("Type")?.Value, out var t) ? t : 0;
                descriptor.Items.Add(new ErDescriptorItem
                {
                    Name           = itemName,
                    Label          = item.Attribute("Label")?.Value,
                    Description    = item.Attribute("Description")?.Value,
                    RawType        = rawType,
                    Type           = Enum.IsDefined(typeof(ErDataType), rawType)
                                     ? (ErDataType)rawType
                                     : ErDataType.Unknown,
                    TypeDescriptor = item.Attribute("TypeDescriptor")?.Value
                });
            }

            // A later duplicate would mean a malformed export; the first definition wins.
            model.Descriptors.TryAdd(descriptor.Name, descriptor);
        }

        return model;
    }

    // ------------------------------------------------------------- model mapping

    private static ErModelMappingSet ReadModelMapping(XElement root)
    {
        var set = new ErModelMappingSet();

        // Navigating from ERModelMappingVersion/Mapping excludes the copies held in the Delta,
        // which carries its own (often empty) ERDataContainerBinding.
        foreach (var version in root.Descendants("ERModelMappingVersion"))
        foreach (var element in version.Elements("Mapping").Elements("ERModelMapping"))
        {
            var definition = new ErMappingDefinition
            {
                Id             = element.Attribute("ID.")?.Value,
                Name           = element.Attribute("Name")?.Value ?? "(unnamed)",
                Description    = element.Attribute("Description")?.Value,
                BaseReference  = element.Attribute("Base")?.Value,
                ModelGuid      = element.Attribute("Model")?.Value,
                ModelName      = element.Attribute("ModelName")?.Value,
                ModelVersion   = VersionOf(element.Attribute("ModelVersion")?.Value),
                RootDescriptor = element.Attribute("DataContainerDescriptor")?.Value,
                Direction      = element.Attribute("Direction")?.Value
            };

            definition.Datasources.AddRange(
                BuildDatasourceTree(element.Element("Datasource")?.Element("ERModelDefinition")));

            var bindingRoot = element.Element("Binding")?.Element("ERDataContainerBinding");
            foreach (var binding in Contents(bindingRoot).Elements("ERDataContainerPathBinding"))
            {
                var bindingPath = binding.Attribute("Path")?.Value;
                if (string.IsNullOrEmpty(bindingPath)) continue;

                var model = new ErModelBinding
                {
                    Path       = bindingPath,
                    Expression = binding.Attribute("ExpressionAsString")?.Value
                };
                model.ReferencedPaths.AddRange(ExpressionPaths.Extract(binding.Element("Expression")));

                definition.Bindings.Add(model);
                definition.BindingsByPath.TryAdd(model.Path, model);
            }

            set.Mappings.Add(definition);
        }

        return set;
    }

    // -------------------------------------------------------------------- format

    private static ErFormat ReadFormat(XElement root)
    {
        var textFormat = root.Descendants("ERTextFormat").FirstOrDefault();

        var format = new ErFormat
        {
            Name        = textFormat?.Attribute("Name")?.Value,
            Id          = textFormat?.Attribute("ID.")?.Value,
            Description = textFormat?.Attribute("Description")?.Value
        };

        var componentRoot = textFormat?.Element("Root")?.Elements().FirstOrDefault();
        if (componentRoot is not null)
            format.Root = ReadComponent(componentRoot);

        var mappingElement = root.Descendants("ERFormatMapping").FirstOrDefault();
        if (mappingElement is not null)
        {
            var mapping = new ErFormatMappingInfo
            {
                Name          = mappingElement.Attribute("Name")?.Value,
                Id            = mappingElement.Attribute("ID.")?.Value,
                FormatVersion = VersionOf(mappingElement.Attribute("FormatVersion")?.Value)
            };

            mapping.Datasources.AddRange(
                BuildDatasourceTree(mappingElement.Element("Datasource")?.Element("ERModelDefinition")));

            var bindingRoot = mappingElement.Element("Binding")?.Element("ERFormatBinding");
            foreach (var binding in Contents(bindingRoot).Elements("ERFormatComponentPropertyBinding"))
            {
                if (!Guid.TryParse(binding.Attribute("Component")?.Value, out var component)) continue;

                var componentBinding = new ErComponentBinding
                {
                    Component    = component,
                    // Absent on value bindings; keep it null so IsValueBinding can tell them apart.
                    PropertyName = binding.Attribute("PropertyName")?.Value,
                    Expression   = binding.Attribute("ExpressionAsString")?.Value
                };
                componentBinding.ReferencedPaths.AddRange(
                    ExpressionPaths.Extract(binding.Element("Expression")));

                if (!mapping.BindingsByComponent.TryGetValue(component, out var list))
                    mapping.BindingsByComponent[component] = list = new List<ErComponentBinding>();

                list.Add(componentBinding);
                mapping.BindingCount++;
            }

            format.Mapping = mapping;
        }

        return format;
    }

    private static ErFormatComponent ReadComponent(XElement element)
    {
        const string prefix = "ERTextFormat";
        var local = element.Name.LocalName;

        var component = new ErFormatComponent
        {
            Kind           = local.StartsWith(prefix, StringComparison.Ordinal)
                             ? local[prefix.Length..]
                             : local,
            Id             = Guid.TryParse(element.Attribute("ID.")?.Value, out var id) ? id : null,
            Name           = element.Attribute("Name")?.Value,
            Value          = element.Attribute("Value")?.Value,
            DateFormat     = element.Attribute("DateFormat")?.Value,
            Encoding       = element.Attribute("Encoding")?.Value,
            Multiplicity   = element.Attribute("Multiplicity")?.Value,
            Transformation = element.Attribute("Transformation")?.Value,

            ExcelRange           = element.Attribute("ExcelRange")?.Value,
            ExcelSheetName       = element.Attribute("ExcelSheetName")?.Value,
            ReplicationDirection = element.Attribute("ReplicationDirection")?.Value,
            Delimiter            = element.Attribute("Delimiter")?.Value,
            MaximalLength        = element.Attribute("MaximalLength")?.Value,
            DataType             = element.Attribute("Type")?.Value
        };

        foreach (var child in Contents(element).Elements())
            if (child.Name.LocalName.StartsWith(prefix, StringComparison.Ordinal))
                component.Children.Add(ReadComponent(child));

        return component;
    }

    // ---------------------------------------------------------- data source tree

    /// <summary>
    /// Rebuilds the data source tree from the flat <c>ERModelItemDefinition</c> list. Nodes name
    /// their parent by path, and that parent is often <i>not</i> itself a declared data source:
    /// a calculated field can hang off a model path such as "Invoice/InvoiceBase". Missing
    /// ancestors are synthesised so the hierarchy still reads the way the designer shows it.
    /// </summary>
    private static List<ErDatasourceNode> BuildDatasourceTree(XElement? modelDefinition)
    {
        var roots = new List<ErDatasourceNode>();
        if (modelDefinition is null) return roots;

        var byPath = new Dictionary<string, ErDatasourceNode>(StringComparer.OrdinalIgnoreCase);
        var declared = new List<(ErDatasourceNode Node, string? ParentPath)>();

        // Pass one records every declared node before anything is attached.
        //
        // Attaching as we read would depend on file order: a child naming a parent that has not
        // been read yet forces that parent to be synthesised, and when the real declaration turns
        // up later it becomes a second node at the same path. The samples do exactly this — one
        // mapping declares $notSentTransactions as a root while 56 children name it as their
        // parent, and the children written before the declaration ended up under a synthetic copy
        // while the rest went under the real one, splitting the branch in two.
        foreach (var definition in Contents(modelDefinition).Elements("ERModelItemDefinition"))
        {
            var valueDefinition = definition.Element("ValueDefinition")?
                                            .Element("ERModelItemValueDefinition");
            var name = valueDefinition?.Attribute("Name")?.Value;
            if (string.IsNullOrEmpty(name)) continue;

            var parentPath = definition.Attribute("ParentPath")?.Value;
            var fullPath   = string.IsNullOrEmpty(parentPath) ? name : $"{parentPath}/{name}";

            var source = valueDefinition!.Element("ValueSource")?.Elements().FirstOrDefault();
            var (kind, detail, expression) = DescribeSource(source);

            var isModelSource = source?.Name.LocalName == "ERModelDataSourceHandler";

            var node = new ErDatasourceNode
            {
                Name            = name,
                FullPath        = fullPath,
                Label           = valueDefinition.Attribute("Label")?.Value,
                Help            = valueDefinition.Attribute("Help")?.Value,
                SourceKind      = kind,
                SourceDetail    = detail,
                Expression      = expression,
                ModelGuid       = isModelSource ? source!.Attribute("ModelGuid")?.Value : null,
                ModelRevision   = isModelSource ? source!.Attribute("RevisionNumber")?.Value : null,
                ModelDescriptor = isModelSource
                                ? source!.Attribute("DataContainerDescriptorName")?.Value
                                : null,
                GroupBy         = ReadGroupBy(source),
                FormatGuid      = source?.Name.LocalName == "ERExportFormatDatasource"
                                ? source.Attribute("FormatGUID")?.Value
                                : null
            };
            node.ReferencedPaths.AddRange(ExpressionPaths.Extract(source));
            node.ResultPaths.AddRange(ExpressionPaths.ExtractResult(source));

            // First declaration wins the index; a repeat still appears in the tree.
            byPath.TryAdd(fullPath, node);
            declared.Add((node, parentPath));
        }

        // Pass two attaches them. Every declared path is now known, so a node is only ever
        // synthesised for an ancestor that genuinely has no declaration of its own.
        foreach (var (node, parentPath) in declared)
            Attach(node, parentPath, byPath, roots);

        return roots;
    }

    private static void Attach(
        ErDatasourceNode node,
        string? parentPath,
        Dictionary<string, ErDatasourceNode> byPath,
        List<ErDatasourceNode> roots)
    {
        if (string.IsNullOrEmpty(parentPath))
        {
            roots.Add(node);
            return;
        }

        if (byPath.TryGetValue(parentPath, out var parent))
        {
            parent.Children.Add(node);
            return;
        }

        // The parent is a model path rather than a declared data source: synthesise the chain.
        var separator = new[] { '/' };
        var segments = parentPath.Split(separator, StringSplitOptions.RemoveEmptyEntries);

        ErDatasourceNode? current = null;
        var built = "";

        foreach (var segment in segments)
        {
            built = built.Length == 0 ? segment : $"{built}/{segment}";

            if (!byPath.TryGetValue(built, out var existing))
            {
                existing = new ErDatasourceNode
                {
                    Name        = segment,
                    FullPath    = built,
                    SourceKind  = "from model",
                    IsSynthetic = true
                };
                byPath[built] = existing;

                if (current is null) roots.Add(existing);
                else current.Children.Add(existing);
            }

            // A declared ancestor found here is placed by its own entry in pass two, so it must
            // not be added again from underneath.
            current = existing;
        }

        current?.Children.Add(node);
    }

    /// <summary>
    /// Reads the grouped fields and aggregations of a group-by data source. Both hang off the
    /// function through a wrapper element and the usual "Contents." collection.
    /// </summary>
    private static ErGroupBySpec? ReadGroupBy(XElement? source)
    {
        if (source is null || source.Name.LocalName != "ERModelGroupByFunction") return null;

        var spec = new ErGroupBySpec
        {
            ListToGroup = source.Attribute("ListToGroup")?.Value,
            SourceListIsAlreadySorted = source.Attribute("SourceListIsAlreadySorted")?.Value == "1"
        };

        var grouped = Contents(source.Element("GroupedFields")?.Element("ERModelGroupByFieldReferences"));
        foreach (var field in grouped.Elements("ERModelGroupByFieldReference"))
        {
            var path = field.Attribute("FieldPath")?.Value;
            if (!string.IsNullOrEmpty(path)) spec.GroupedFields.Add(path);
        }

        var aggregations = Contents(source.Element("Aggregations")?.Element("ERModelGroupByAggregations"));
        foreach (var aggregation in aggregations.Elements("ERModelGroupByAggregation"))
        {
            var path = aggregation.Attribute("FieldPath")?.Value;
            if (string.IsNullOrEmpty(path)) continue;

            var raw = int.TryParse(aggregation.Attribute("SelectionField")?.Value, out var v) ? v : 0;

            spec.Aggregations.Add(new ErAggregation
            {
                Name      = aggregation.Attribute("Name")?.Value,
                FieldPath = path,
                RawKind   = raw,
                Kind      = Enum.IsDefined(typeof(ErAggregationKind), raw)
                            ? (ErAggregationKind)raw
                            : ErAggregationKind.Unknown
            });
        }

        return spec;
    }

    private static (string? Kind, string? Detail, string? Expression) DescribeSource(XElement? source)
    {
        if (source is null) return (null, null, null);

        string? A(string name) => source.Attribute(name)?.Value;

        return source.Name.LocalName switch
        {
            "ERModelDataSourceHandler"          => ("Model",       Join(A("DataContainerDescriptorName"), Rev(A("RevisionNumber"))), null),
            "ERModelEnumDataSourceHandler"      => ("Model enum",  Join(A("ModelEnumName"), Rev(A("RevisionNumber"))),               null),
            "ERTableDataSourceHandler"          => ("Table",       A("Table") ?? A("Path"),         null),
            "ERTableDataSource"                 => ("Table",       A("Table") ?? A("Path"),         null),
            "ERClassDataSourceHandler"          => ("Class",       A("ClassName"),                  null),
            "ERObjectDataSourceHandler"         => ("Object",      A("ClassName"),                  null),
            "EREnumDataSourceHandler"           => ("Enum",        A("EnumName"),                   null),
            "ERUserParameterDataSourceHandler"  => ("Parameter",   A("ExtendedDataTypeName"),       null),
            "EREnumParameterDataSourceHandler"  => ("Enum parameter", A("EnumName") ?? A("ModelEnumName"), null),
            "EREmptyContainerDataSourceHandler" => ("Container",   null,                            null),
            "ERModelExpressionItem"             => ("Calculated",  null,                            A("ExpressionAsString")),
            "ERModelGroupByFunction"            => ("Group by",    A("ListToGroup"),                null),
            "ERExportFormatDatasource"          => ("Format",      null,                            null),
            "ERDataCollectionDatasource"        => ("Collection",  A("ItemType") is { } t ? $"item type {t}" : null, null),
            "ERJoinedList"                      => ("Joined list", A("Path"),                       null),
            "ERListJoinDatasource"              => ("Joined list", A("Path"),                       null),
            _                                   => (source.Name.LocalName, null,                   A("ExpressionAsString"))
        };
    }

    private static string? Rev(string? revision) =>
        string.IsNullOrEmpty(revision) ? null : $"v{revision}";

    private static string? Join(params string?[] parts)
    {
        var text = string.Join(" ", parts.Where(p => !string.IsNullOrEmpty(p)));
        return text.Length == 0 ? null : text;
    }

    /// <summary>The "Contents." collection wrapper, or an empty stand-in when absent.</summary>
    private static XElement Contents(XElement? parent) =>
        parent?.Element("Contents.") ?? new XElement("empty");

    private static string? VersionOf(string? reference)
    {
        if (string.IsNullOrEmpty(reference)) return null;
        var comma = reference.LastIndexOf(',');
        return comma >= 0 && comma < reference.Length - 1 ? reference[(comma + 1)..] : null;
    }
}
