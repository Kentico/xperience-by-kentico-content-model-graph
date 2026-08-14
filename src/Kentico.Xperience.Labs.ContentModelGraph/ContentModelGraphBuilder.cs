using System.Text.Json;
using System.Xml.Linq;

using CMS.ContentEngine;
using CMS.DataEngine;
using CMS.FormEngine;
using CMS.Modules;

namespace Kentico.Xperience.Labs.ContentModelGraph;

public sealed class ContentModelGraphBuilder(IInfoProvider<TaxonomyInfo> taxonomyInfoProvider) : IContentModelGraphBuilder
{
    private const string SCHEMA_REGISTRY_CLASS_NAME = "CMS.ContentItemCommonData";
    private const string CLASS_TYPE_CONTENT = "Content";
    private const string CLASS_TYPE_CUSTOMER_JOURNEY = "CJ";
    private const string CLASS_TYPE_FORM = "Form";
    private const string SYSTEM_RESOURCE_NAME = "CMS";

    public async Task<GraphData> Build()
    {
        var classes = (await DataClassInfoProvider.ProviderObject.Get()
            .Columns(nameof(DataClassInfo.ClassID),
                     nameof(DataClassInfo.ClassName),
                     nameof(DataClassInfo.ClassDisplayName),
                     nameof(DataClassInfo.ClassType),
                     nameof(DataClassInfo.ClassContentTypeType),
                     nameof(DataClassInfo.ClassGUID),
                     nameof(DataClassInfo.ClassResourceID),
                     nameof(DataClassInfo.ClassFormDefinition))
            .GetEnumerableTypedResultAsync())
            .ToList();

        var systemResources = await GetSystemResources();
        var definitions = classes.ToDictionary(c => c.ClassName, ParseDefinition, StringComparer.OrdinalIgnoreCase);
        var classNamesByGuid = classes.ToDictionary(c => c.ClassGUID, c => c.ClassName);
        var nodes = new Dictionary<string, GraphNode>(StringComparer.OrdinalIgnoreCase);
        var edges = new Dictionary<string, EdgeAccumulator>(StringComparer.OrdinalIgnoreCase);

        var taxonomyNames = await AddTaxonomyNodes(nodes);
        var schemaNames = AddSchemaNodes(definitions, nodes);
        AddSchemaTaxonomyEdges(definitions, schemaNames, taxonomyNames, edges);

        foreach (var dataClass in classes)
        {
            if (string.Equals(dataClass.ClassName, SCHEMA_REGISTRY_CLASS_NAME, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var definition = definitions[dataClass.ClassName];
            if (definition is null)
            {
                continue;
            }

            var fields = definition.Elements("field").ToList();
            string nodeId = ClassNodeId(dataClass.ClassName);

            nodes[nodeId] = new GraphNode
            {
                Id = nodeId,
                Name = dataClass.ClassName,
                DisplayName = dataClass.ClassDisplayName,
                Kind = ResolveNodeKind(dataClass, systemResources),
                SystemObjectTypeGroup = ResolveSystemObjectTypeGroup(dataClass, systemResources),
                FieldCount = fields.Count(field =>
                    field.Attribute("system")?.Value != "true" && field.Attribute("isPK")?.Value != "true")
            };

            AddAssignedSchemaEdges(definition, nodeId, schemaNames, edges);
            AddFieldEdges(
                fields,
                nodeId,
                classNamesByGuid,
                schemaNames,
                taxonomyNames,
                string.Equals(dataClass.ClassType, CLASS_TYPE_CONTENT, StringComparison.OrdinalIgnoreCase),
                edges);
        }

        await AddAlternativeFormEdges(
            classes.ToDictionary(c => c.ClassID, c => c.ClassName),
            classNamesByGuid,
            schemaNames,
            taxonomyNames,
            edges);

        var resolvedEdges = edges.Values
            .Where(edge => nodes.ContainsKey(edge.Source) && nodes.ContainsKey(edge.Target))
            .Select(edge => edge.ToEdge())
            .ToList();

        return new GraphData { Nodes = nodes.Values.ToList(), Edges = resolvedEdges };
    }

    private async Task<Dictionary<Guid, string>> AddTaxonomyNodes(IDictionary<string, GraphNode> nodes)
    {
        var taxonomies = await taxonomyInfoProvider.Get()
            .Columns(nameof(TaxonomyInfo.TaxonomyGUID), nameof(TaxonomyInfo.TaxonomyName), nameof(TaxonomyInfo.TaxonomyTitle))
            .GetEnumerableTypedResultAsync();
        var taxonomyNames = new Dictionary<Guid, string>();

        foreach (var taxonomy in taxonomies)
        {
            taxonomyNames[taxonomy.TaxonomyGUID] = taxonomy.TaxonomyName;
            nodes[TaxonomyNodeId(taxonomy.TaxonomyGUID)] = new GraphNode
            {
                Id = TaxonomyNodeId(taxonomy.TaxonomyGUID),
                Name = taxonomy.TaxonomyName,
                DisplayName = taxonomy.TaxonomyTitle,
                Kind = GraphNodeKind.TAXONOMY
            };
        }

        return taxonomyNames;
    }

    private static Dictionary<Guid, string> AddSchemaNodes(
        IDictionary<string, XElement?> definitions,
        IDictionary<string, GraphNode> nodes)
    {
        var schemaNames = new Dictionary<Guid, string>();

        if (!definitions.TryGetValue(SCHEMA_REGISTRY_CLASS_NAME, out var registry) || registry is null)
        {
            return schemaNames;
        }

        foreach (var schema in registry.Elements("schema"))
        {
            if (!Guid.TryParse(schema.Attribute("guid")?.Value, out var guid))
            {
                continue;
            }

            string name = schema.Attribute("name")?.Value ?? guid.ToString();
            string? caption = schema.Element("properties")?.Element("fieldcaption")?.Value;
            int fieldCount = registry.Elements("field")
                .Count(field => string.Equals(
                    field.Element("properties")?.Element("kxp_schema_identifier")?.Value,
                    guid.ToString(),
                    StringComparison.OrdinalIgnoreCase));

            schemaNames[guid] = name;
            string nodeId = SchemaNodeId(guid);
            nodes[nodeId] = new GraphNode
            {
                Id = nodeId,
                Name = name,
                DisplayName = string.IsNullOrEmpty(caption) ? name : caption,
                Kind = GraphNodeKind.SCHEMA,
                FieldCount = fieldCount
            };
        }

        return schemaNames;
    }

    private static void AddAssignedSchemaEdges(
        XElement definition,
        string nodeId,
        IDictionary<Guid, string> schemaNames,
        IDictionary<string, EdgeAccumulator> edges)
    {
        foreach (var schema in definition.Elements("schema"))
        {
            if (Guid.TryParse(schema.Attribute("guid")?.Value, out var guid) && schemaNames.ContainsKey(guid))
            {
                AddEdge(edges, nodeId, SchemaNodeId(guid), GraphEdgeKind.SCHEMA_ASSIGNMENT, "uses schema");
            }
        }
    }

    private static void AddSchemaTaxonomyEdges(
        IDictionary<string, XElement?> definitions,
        IDictionary<Guid, string> schemaNames,
        IDictionary<Guid, string> taxonomyNames,
        IDictionary<string, EdgeAccumulator> edges)
    {
        if (!definitions.TryGetValue(SCHEMA_REGISTRY_CLASS_NAME, out var registry) || registry is null)
        {
            return;
        }

        foreach (var field in registry.Elements("field"))
        {
            if (Guid.TryParse(field.Element("properties")?.Element("kxp_schema_identifier")?.Value, out var schemaGuid)
                && schemaNames.ContainsKey(schemaGuid))
            {
                AddTaxonomyFieldEdges([field], SchemaNodeId(schemaGuid), taxonomyNames, edges);
            }
        }
    }

    private static void AddFieldEdges(
        IEnumerable<XElement> fields,
        string nodeId,
        IDictionary<Guid, string> classNamesByGuid,
        IDictionary<Guid, string> schemaNames,
        IDictionary<Guid, string> taxonomyNames,
        bool includeTaxonomyReferences,
        IDictionary<string, EdgeAccumulator> edges)
    {
        var fieldList = fields.ToList();

        foreach (var field in fieldList)
        {
            string fieldName = field.Attribute("column")?.Value ?? string.Empty;
            var settings = field.Element("settings");

            foreach (var guid in ReadGuidList(settings?.Element("AllowedContentItemTypeIdentifiers")?.Value))
            {
                if (classNamesByGuid.TryGetValue(guid, out string? targetClassName))
                {
                    AddEdge(edges, nodeId, ClassNodeId(targetClassName), GraphEdgeKind.CONTENT_REFERENCE, fieldName);
                }
            }

            foreach (var guid in ReadGuidList(settings?.Element("AllowedSchemaIdentifiers")?.Value).Where(schemaNames.ContainsKey))
            {
                AddEdge(edges, nodeId, SchemaNodeId(guid), GraphEdgeKind.SCHEMA_REFERENCE, fieldName);
            }

            string? referencedObjectType = field.Attribute("refobjtype")?.Value;
            if (!string.IsNullOrEmpty(referencedObjectType))
            {
                AddEdge(edges, nodeId, ClassNodeId(referencedObjectType), GraphEdgeKind.OBJECT_REFERENCE, fieldName);
            }
        }

        if (includeTaxonomyReferences)
        {
            AddTaxonomyFieldEdges(fieldList, nodeId, taxonomyNames, edges);
        }
    }

    private static void AddTaxonomyFieldEdges(
        IEnumerable<XElement> fields,
        string nodeId,
        IDictionary<Guid, string> taxonomyNames,
        IDictionary<string, EdgeAccumulator> edges)
    {
        foreach (var field in fields.Where(field =>
            string.Equals(field.Attribute("columntype")?.Value, "taxonomy", StringComparison.OrdinalIgnoreCase)))
        {
            string fieldName = field.Attribute("column")?.Value ?? string.Empty;
            foreach (var guid in ReadGuidList(field.Element("settings")?.Element("TaxonomyGroup")?.Value).Where(taxonomyNames.ContainsKey))
            {
                AddEdge(edges, nodeId, TaxonomyNodeId(guid), GraphEdgeKind.TAXONOMY_REFERENCE, fieldName);
            }
        }
    }

    private static async Task AddAlternativeFormEdges(
        IDictionary<int, string> classNamesById,
        IDictionary<Guid, string> classNamesByGuid,
        IDictionary<Guid, string> schemaNames,
        IDictionary<Guid, string> taxonomyNames,
        IDictionary<string, EdgeAccumulator> edges)
    {
        var forms = await AlternativeFormInfoProvider.ProviderObject.Get()
            .Columns(nameof(AlternativeFormInfo.FormClassID), nameof(AlternativeFormInfo.FormDefinition))
            .GetEnumerableTypedResultAsync();

        foreach (var form in forms)
        {
            if (!classNamesById.TryGetValue(form.FormClassID, out string? className))
            {
                continue;
            }

            var definition = ParseXml(form.FormDefinition);
            if (definition is null)
            {
                continue;
            }

            AddFieldEdges(
                definition.Elements("field"),
                ClassNodeId(className),
                classNamesByGuid,
                schemaNames,
                taxonomyNames,
                false,
                edges);
        }
    }

    private static void AddEdge(
        IDictionary<string, EdgeAccumulator> edges,
        string source,
        string target,
        string kind,
        string label)
    {
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string id = $"{source}|{target}|{kind}";
        if (!edges.TryGetValue(id, out var edge))
        {
            edge = new EdgeAccumulator { Id = id, Source = source, Target = target, Kind = kind };
            edges[id] = edge;
        }

        if (!string.IsNullOrEmpty(label))
        {
            edge.Labels.Add(label);
        }
    }

    private static IEnumerable<Guid> ReadGuidList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<Guid[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static XElement? ParseDefinition(DataClassInfo dataClass) => ParseXml(dataClass.ClassFormDefinition);

    private static XElement? ParseXml(string? formDefinition)
    {
        if (string.IsNullOrWhiteSpace(formDefinition))
        {
            return null;
        }

        try
        {
            return XDocument.Parse(formDefinition).Root;
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    private static async Task<Dictionary<int, string>> GetSystemResources()
    {
        var resources = await ResourceInfo.Provider.Get()
            .Columns(nameof(ResourceInfo.ResourceID), nameof(ResourceInfo.ResourceName))
            .GetEnumerableTypedResultAsync();

        return resources
            .Where(resource => IsSystemResource(resource.ResourceName))
            .ToDictionary(resource => resource.ResourceID, resource => resource.ResourceName);
    }

    private static bool IsSystemResource(string resourceName) =>
        IsResourceInGroup(resourceName, SYSTEM_RESOURCE_NAME)
        || IsResourceInGroup(resourceName, SystemObjectTypeGroup.EMAIL_LIBRARY)
        || IsResourceInGroup(resourceName, SystemObjectTypeGroup.MARKETING)
        || IsResourceInGroup(resourceName, SystemObjectTypeGroup.COMMERCE)
        || IsResourceInGroup(resourceName, SystemObjectTypeGroup.AIRA)
        || IsResourceInGroup(resourceName, SystemObjectTypeGroup.OTHER)
        || IsResourceInGroup(resourceName, "CI")
        || IsResourceInGroup(resourceName, "Media")
        || IsResourceInGroup(resourceName, "Temp")
        || IsResourceInGroup(resourceName, "BizForm")
        || IsResourceInGroup(resourceName, "CJ");

    private static bool IsResourceInGroup(string resourceName, string groupName) =>
        string.Equals(resourceName, groupName, StringComparison.OrdinalIgnoreCase)
        || resourceName.StartsWith(groupName + ".", StringComparison.OrdinalIgnoreCase);

    private static string ResolveNodeKind(DataClassInfo dataClass, IDictionary<int, string> systemResources)
    {
        if (IsClassInGroup(dataClass.ClassName, "BizForm"))
        {
            return GraphNodeKind.FORMS;
        }

        if (!string.Equals(dataClass.ClassType, CLASS_TYPE_CONTENT, StringComparison.OrdinalIgnoreCase))
        {
            return systemResources.ContainsKey(dataClass.ClassResourceID)
                   || IsKnownSystemClass(dataClass.ClassName)
                   || string.Equals(dataClass.ClassType, CLASS_TYPE_FORM, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(dataClass.ClassType, CLASS_TYPE_CUSTOMER_JOURNEY, StringComparison.OrdinalIgnoreCase)
                ? GraphNodeKind.SYSTEM_OBJECT_TYPE
                : GraphNodeKind.OBJECT_TYPE;
        }

        return dataClass.ClassContentTypeType?.ToLowerInvariant() switch
        {
            "website" => GraphNodeKind.WEBSITE,
            "reusable" => GraphNodeKind.REUSABLE,
            "email" => GraphNodeKind.EMAIL,
            "headless" => GraphNodeKind.HEADLESS,
            _ => GraphNodeKind.OBJECT_TYPE
        };
    }

    private static string? ResolveSystemObjectTypeGroup(
        DataClassInfo dataClass,
        IDictionary<int, string> systemResources)
    {
        if (!string.Equals(
            ResolveNodeKind(dataClass, systemResources),
            GraphNodeKind.SYSTEM_OBJECT_TYPE,
            StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string? classGroup = ResolveSystemObjectTypeGroup(dataClass.ClassName);
        if (classGroup is not null)
        {
            return classGroup;
        }

        if (systemResources.TryGetValue(dataClass.ClassResourceID, out string? resourceName))
        {
            if (IsResourceInGroup(resourceName, SystemObjectTypeGroup.EMAIL_LIBRARY))
            {
                return SystemObjectTypeGroup.EMAIL_LIBRARY;
            }

            if (IsResourceInGroup(resourceName, SystemObjectTypeGroup.MARKETING))
            {
                return SystemObjectTypeGroup.MARKETING;
            }

            if (IsResourceInGroup(resourceName, SystemObjectTypeGroup.COMMERCE))
            {
                return SystemObjectTypeGroup.COMMERCE;
            }

            if (IsResourceInGroup(resourceName, SystemObjectTypeGroup.AIRA))
            {
                return SystemObjectTypeGroup.AIRA;
            }

            if (IsResourceInGroup(resourceName, SystemObjectTypeGroup.OTHER))
            {
                return SystemObjectTypeGroup.OTHER;
            }

            if (IsResourceInGroup(resourceName, SYSTEM_RESOURCE_NAME))
            {
                return SystemObjectTypeGroup.CMS;
            }
        }

        if (string.Equals(dataClass.ClassType, CLASS_TYPE_FORM, StringComparison.OrdinalIgnoreCase)
            || string.Equals(dataClass.ClassType, CLASS_TYPE_CUSTOMER_JOURNEY, StringComparison.OrdinalIgnoreCase))
        {
            return SystemObjectTypeGroup.CMS;
        }

        return null;
    }

    private static string? ResolveSystemObjectTypeGroup(string className)
    {
        if (IsClassInGroup(className, SystemObjectTypeGroup.EMAIL_LIBRARY))
        {
            return SystemObjectTypeGroup.EMAIL_LIBRARY;
        }

        if (IsClassInGroup(className, SystemObjectTypeGroup.MARKETING))
        {
            return SystemObjectTypeGroup.MARKETING;
        }

        if (IsClassInGroup(className, SystemObjectTypeGroup.COMMERCE))
        {
            return SystemObjectTypeGroup.COMMERCE;
        }

        if (IsClassInGroup(className, SystemObjectTypeGroup.AIRA))
        {
            return SystemObjectTypeGroup.AIRA;
        }

        if (IsClassInGroup(className, "CJ"))
        {
            return SystemObjectTypeGroup.MARKETING;
        }

        if (IsClassInGroup(className, "CI")
            || IsClassInGroup(className, "Media")
            || IsClassInGroup(className, "Temp"))
        {
            return SystemObjectTypeGroup.OTHER;
        }

        if (IsClassInGroup(className, SYSTEM_RESOURCE_NAME))
        {
            return SystemObjectTypeGroup.CMS;
        }

        return null;
    }

    private static bool IsKnownSystemClass(string className) => ResolveSystemObjectTypeGroup(className) is not null;

    private static bool IsClassInGroup(string className, string groupName) =>
        string.Equals(className, groupName, StringComparison.OrdinalIgnoreCase)
        || className.StartsWith(groupName + ".", StringComparison.OrdinalIgnoreCase);

    private static string ClassNodeId(string className) => $"class:{className.ToLowerInvariant()}";

    private static string SchemaNodeId(Guid schemaGuid) => $"schema:{schemaGuid}";

    private static string TaxonomyNodeId(Guid taxonomyGuid) => $"taxonomy:{taxonomyGuid}";

    private sealed class EdgeAccumulator
    {
        public string Id { get; set; } = string.Empty;

        public string Source { get; set; } = string.Empty;

        public string Target { get; set; } = string.Empty;

        public string Kind { get; set; } = string.Empty;

        public SortedSet<string> Labels { get; } = new(StringComparer.OrdinalIgnoreCase);

        public GraphEdge ToEdge() => new()
        {
            Id = Id,
            Source = Source,
            Target = Target,
            Kind = Kind,
            Label = string.Join(", ", Labels)
        };
    }
}
