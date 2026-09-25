namespace Kentico.Xperience.ContentModelGraph;

internal static class GraphNodeKind
{
    public const string WEBSITE = "website";
    public const string REUSABLE = "reusable";
    public const string EMAIL = "email";
    public const string HEADLESS = "headless";
    public const string SCHEMA = "schema";
    public const string TAXONOMY = "taxonomy";
    public const string FORMS = "forms";
    public const string OBJECT_TYPE = "objectType";
    public const string SYSTEM_OBJECT_TYPE = "systemObjectType";
}

internal static class SystemObjectTypeGroup
{
    public const string CMS = "CMS";
    public const string EMAIL_LIBRARY = "EmailLibrary";
    public const string MARKETING = "OM";
    public const string COMMERCE = "Commerce";
    public const string AIRA = "AIRA";
    public const string OTHER = "Other";
}

internal static class GraphEdgeKind
{
    public const string SCHEMA_ASSIGNMENT = "schemaAssignment";
    public const string CONTENT_REFERENCE = "contentReference";
    public const string SCHEMA_REFERENCE = "schemaReference";
    public const string OBJECT_REFERENCE = "objectReference";
    public const string TAXONOMY_REFERENCE = "taxonomyReference";
}

public sealed class GraphNode
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? AdminUrl { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string? SystemObjectTypeGroup { get; set; }

    public int FieldCount { get; set; }

    /// <summary>
    /// Total number of fields contributed by the reusable field schemas assigned to this node,
    /// or <c>null</c> when the node has no assigned schemas and so has no schema fields to report.
    /// </summary>
    public int? SchemaFieldCount { get; set; }
}

public sealed class GraphEdge
{
    public string Id { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string Target { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;
}

public sealed class GraphData
{
    public IReadOnlyCollection<GraphNode> Nodes { get; set; } = [];

    public IReadOnlyCollection<GraphEdge> Edges { get; set; } = [];

    /// <summary>
    /// Identifier of the node the graph was filtered down to, which the client marks as the current item,
    /// or <c>null</c> when the graph is not focused on any one node - as on the whole content model graph,
    /// which is every node the application knows about and singles none of them out.
    /// </summary>
    public string? FocalNodeId { get; set; }
}
