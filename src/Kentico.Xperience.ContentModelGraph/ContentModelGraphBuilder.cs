using System.Text.Json;
using System.Xml.Linq;

using CMS.ContentEngine;
using CMS.DataEngine;
using CMS.FormEngine;
using CMS.Modules;
using CMS.OnlineForms;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.UIPages;

namespace Kentico.Xperience.ContentModelGraph;

public sealed class ContentModelGraphBuilder(
    IInfoProvider<TaxonomyInfo> taxonomyInfoProvider,
    IInfoProvider<BizFormInfo> bizFormInfoProvider,
    IPageLinkGenerator pageLinkGenerator) : IContentModelGraphBuilder
{
    private const string SCHEMA_REGISTRY_CLASS_NAME = "CMS.ContentItemCommonData";
    private const string CLASS_TYPE_CONTENT = "Content";
    private const string CLASS_TYPE_CUSTOMER_JOURNEY = "CJ";
    private const string CLASS_TYPE_FORM = "Form";
    private const string SYSTEM_RESOURCE_NAME = "CMS";

    public async Task<GraphData> Build(GraphApplicationAccess applications)
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

        var taxonomyNames = await AddTaxonomyNodes(nodes, applications);
        // Schema nodes are built before the class loop below, so their field counts are already
        // known by the time a class needs to total up the schemas assigned to it.
        var schemaNames = AddSchemaNodes(definitions, nodes, applications, out var schemaFieldCounts);
        AddSchemaTaxonomyEdges(definitions, schemaNames, taxonomyNames, edges);
        // Resolved for the whole graph in one query, before the loop, so that a form class can be linked to
        // its form without a query per node.
        var formIdsByClassId = await GetFormIdsByClassId(classes, systemResources, applications.Forms);

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
            string nodeKind = ResolveNodeKind(dataClass, systemResources);

            nodes[nodeId] = new GraphNode
            {
                Id = nodeId,
                Name = dataClass.ClassName,
                DisplayName = dataClass.ClassDisplayName,
                AdminUrl = GetClassAdminUrl(
                    dataClass.ClassID,
                    dataClass.ClassResourceID,
                    dataClass.ClassType,
                    nodeKind,
                    formIdsByClassId,
                    applications),
                Kind = nodeKind,
                SystemObjectTypeGroup = ResolveSystemObjectTypeGroup(dataClass, systemResources),
                FieldCount = fields.Count(field =>
                    field.Attribute("system")?.Value != "true" && field.Attribute("isPK")?.Value != "true"),
                SchemaFieldCount = SumAssignedSchemaFieldCounts(definition, schemaFieldCounts)
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

    private async Task<Dictionary<Guid, string>> AddTaxonomyNodes(
        IDictionary<string, GraphNode> nodes,
        GraphApplicationAccess applications)
    {
        var taxonomies = await taxonomyInfoProvider.Get()
            .Columns(
                nameof(TaxonomyInfo.TaxonomyID),
                nameof(TaxonomyInfo.TaxonomyGUID),
                nameof(TaxonomyInfo.TaxonomyName),
                nameof(TaxonomyInfo.TaxonomyTitle))
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
                AdminUrl = GetTaxonomyAdminUrl(taxonomy.TaxonomyID, applications),
                Kind = GraphNodeKind.TAXONOMY
            };
        }

        return taxonomyNames;
    }

    private Dictionary<Guid, string> AddSchemaNodes(
        IDictionary<string, XElement?> definitions,
        IDictionary<string, GraphNode> nodes,
        GraphApplicationAccess applications,
        out Dictionary<Guid, int> schemaFieldCounts)
    {
        var schemaNames = new Dictionary<Guid, string>();
        schemaFieldCounts = [];

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
            schemaFieldCounts[guid] = fieldCount;
            string nodeId = SchemaNodeId(guid);
            nodes[nodeId] = new GraphNode
            {
                Id = nodeId,
                Name = name,
                DisplayName = string.IsNullOrEmpty(caption) ? name : caption,
                AdminUrl = GetReusableFieldSchemaAdminUrl(guid, applications),
                Kind = GraphNodeKind.SCHEMA,
                FieldCount = fieldCount
            };
        }

        return schemaNames;
    }

    /// <summary>
    /// Totals the fields contributed by every reusable field schema assigned to a class.
    /// Returns <c>null</c> when the class has no assigned schemas, so that callers can omit
    /// the schema field count rather than reporting a misleading zero.
    /// </summary>
    private static int? SumAssignedSchemaFieldCounts(
        XElement definition,
        IDictionary<Guid, int> schemaFieldCounts)
    {
        int? total = null;

        foreach (var schema in definition.Elements("schema"))
        {
            if (Guid.TryParse(schema.Attribute("guid")?.Value, out var guid)
                && schemaFieldCounts.TryGetValue(guid, out int fieldCount))
            {
                total = (total ?? 0) + fieldCount;
            }
        }

        return total;
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
            string fieldName = ContentModelGraphFieldLabel.Resolve(field);
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
            string fieldName = ContentModelGraphFieldLabel.Resolve(field);
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

    /// <summary>
    /// The administration page a class node links to, and the application whose access governs that link.
    /// Three destinations, not two: a content type is edited in the Content types application, a form class
    /// belongs to a form authored in the Forms application, and everything left is an object type edited in
    /// the Modules application.
    /// </summary>
    /// <remarks>
    /// Form classes used to fall into the Modules branch, which sent the <i>Contact Us</i> form to a module
    /// class definition rather than to its form builder. They are recognized here by the node kind that
    /// <see cref="ResolveNodeKind" /> already resolved, so the two never disagree about what a form is.
    /// </remarks>
    /// <param name="classId">The class the node was built from.</param>
    /// <param name="classResourceId">The module that owns the class, which addresses the Modules page.</param>
    /// <param name="classType">The class type, which is what identifies a content type.</param>
    /// <param name="nodeKind">
    /// The node kind <see cref="ResolveNodeKind" /> gave this class, which is what identifies a form class.
    /// </param>
    /// <param name="formIdsByClassId">
    /// The forms belonging to the graph's form classes, resolved once by <see cref="GetFormIdsByClassId" />.
    /// A form class with no form in it - the Forms application cannot address one - yields no link rather
    /// than a link that would not resolve.
    /// </param>
    /// <param name="applications">The applications the current user may open.</param>
    internal string? GetClassAdminUrl(
        int classId,
        int classResourceId,
        string? classType,
        string nodeKind,
        IReadOnlyDictionary<int, int> formIdsByClassId,
        GraphApplicationAccess applications)
    {
        if (string.Equals(classType, CLASS_TYPE_CONTENT, StringComparison.OrdinalIgnoreCase))
        {
            return ContentModelGraphApplicationLinks.ForContentType(
                applications,
                () => AdminUrlHelper.EnsureAdminPrefix(pageLinkGenerator.GetPath<ContentTypeFields>(
                    new PageParameterValues { { typeof(ContentTypeEditSection), classId } })));
        }

        if (string.Equals(nodeKind, GraphNodeKind.FORMS, StringComparison.OrdinalIgnoreCase))
        {
            return formIdsByClassId.TryGetValue(classId, out int formId)
                ? ContentModelGraphApplicationLinks.ForForm(
                    applications,
                    () => ContentItemRelationshipGraphBuilder.GetFormAdminUrl(pageLinkGenerator, formId))
                : null;
        }

        return ContentModelGraphApplicationLinks.ForObjectType(
            applications,
            () => AdminUrlHelper.EnsureAdminPrefix(pageLinkGenerator.GetPath<ClassFields>(
                new PageParameterValues
                {
                    { typeof(ModuleEditSection), classResourceId },
                    { typeof(ClassEditSection), classId }
                })));
    }

    /// <summary>
    /// The form each of the graph's form classes belongs to. This graph is built from classes, while the
    /// Forms application addresses forms, so the link needs the inverse of the class lookup: one batched
    /// query over every form-kind class rather than a query per node, in the style of the relationships
    /// graph's tag URLs. A user who cannot open the Forms application gets no map and the query is skipped -
    /// the form nodes keep their labels, just not their links.
    /// </summary>
    private async Task<IReadOnlyDictionary<int, int>> GetFormIdsByClassId(
        IEnumerable<DataClassInfo> classes,
        IDictionary<int, string> systemResources,
        bool isFormsApplicationAccessible)
    {
        if (!isFormsApplicationAccessible)
        {
            return new Dictionary<int, int>();
        }

        int[] formClassIds = [.. classes
            .Where(dataClass => string.Equals(
                ResolveNodeKind(dataClass, systemResources),
                GraphNodeKind.FORMS,
                StringComparison.OrdinalIgnoreCase))
            .Select(dataClass => dataClass.ClassID)
            .Distinct()];
        if (formClassIds.Length == 0)
        {
            return new Dictionary<int, int>();
        }

        var forms = await bizFormInfoProvider.Get()
            .Columns(nameof(BizFormInfo.FormID), nameof(BizFormInfo.FormClassID))
            .WhereIn(nameof(BizFormInfo.FormClassID), formClassIds)
            .GetEnumerableTypedResultAsync();

        // A class backs at most one form, but grouping keeps a duplicated row from throwing rather than
        // costing one node its link.
        return forms
            .GroupBy(form => form.FormClassID)
            .ToDictionary(group => group.Key, group => group.First().FormID);
    }

    internal string? GetTaxonomyAdminUrl(int taxonomyId, GraphApplicationAccess applications) =>
        ContentModelGraphApplicationLinks.ForTaxonomy(
            applications,
            () => AdminUrlHelper.EnsureAdminPrefix(pageLinkGenerator.GetPath<TaxonomyEdit>(
                new PageParameterValues { { typeof(TaxonomyEditSection), taxonomyId } })));

    internal string? GetReusableFieldSchemaAdminUrl(Guid schemaGuid, GraphApplicationAccess applications) =>
        ContentModelGraphApplicationLinks.ForReusableFieldSchema(
            applications,
            () => AdminUrlHelper.EnsureAdminPrefix(pageLinkGenerator.GetPath<ReusableFieldSchemaFields>(
                new PageParameterValues { { typeof(ReusableFieldSchemaEditSection), schemaGuid } })));

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

/// <summary>
/// Which application governs each administration link the content model graph emits, and so which flag of
/// <see cref="GraphApplicationAccess" /> decides whether the link is offered at all. A denied application
/// costs a node its link, not its label.
/// </summary>
/// <remarks>
/// Reusable field schemas are not an application of their own: they are pages of the Content types
/// application - <c>ReusableFieldSchemaList</c> is registered under <c>ContentTypesApplication</c> with the
/// <c>reusable-field-schemas</c> slug, and the edit section and its fields tab hang off that list - so the
/// same flag governs them as governs content types. Pinned by a guard test rather than assumed.
/// Form classes are the other case worth naming: they are not Modules classes that happen to hold form
/// data, they are the storage behind a form authored in the Forms application, so the Forms flag governs
/// them and the Modules flag governs only what is left.
/// Suppression goes through <see cref="ContentItemRelationshipGraphBuilder.GetApplicationLink" />, shared
/// with the relationships graph rather than copied: it is what keeps a denied application from costing a
/// page link generation per node, of which this graph has one for every content type, schema and taxonomy.
/// </remarks>
internal static class ContentModelGraphApplicationLinks
{
    internal static string? ForContentType(GraphApplicationAccess applications, Func<string> getAdminUrl) =>
        ContentItemRelationshipGraphBuilder.GetApplicationLink(applications.ContentTypes, getAdminUrl);

    internal static string? ForReusableFieldSchema(GraphApplicationAccess applications, Func<string> getAdminUrl) =>
        ContentItemRelationshipGraphBuilder.GetApplicationLink(applications.ContentTypes, getAdminUrl);

    internal static string? ForTaxonomy(GraphApplicationAccess applications, Func<string> getAdminUrl) =>
        ContentItemRelationshipGraphBuilder.GetApplicationLink(applications.Taxonomy, getAdminUrl);

    /// <summary>
    /// A form class links to its form builder in the Forms application, where forms are authored - not to a
    /// class definition in Modules, which is where the "everything that is not a content type" branch used
    /// to send it.
    /// </summary>
    internal static string? ForForm(GraphApplicationAccess applications, Func<string> getAdminUrl) =>
        ContentItemRelationshipGraphBuilder.GetApplicationLink(applications.Forms, getAdminUrl);

    /// <summary>
    /// Every remaining class is an object type, edited in the Modules application. Access to the application
    /// is the whole gate - the individual class definitions within it are not separately governed.
    /// </summary>
    internal static string? ForObjectType(GraphApplicationAccess applications, Func<string> getAdminUrl) =>
        ContentItemRelationshipGraphBuilder.GetApplicationLink(applications.Modules, getAdminUrl);
}

internal static class AdminUrlHelper
{
    public static string EnsureAdminPrefix(string path)
    {
        if (path.Equals("/admin", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/admin/", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return $"/admin/{path.TrimStart('/')}";
    }
}

internal static class ContentModelGraphFieldLabel
{
    public static string Resolve(XElement field)
    {
        string? caption = field.Element("properties")?.Element("fieldcaption")?.Value;

        return string.IsNullOrWhiteSpace(caption)
            ? field.Attribute("column")?.Value ?? string.Empty
            : caption;
    }
}
