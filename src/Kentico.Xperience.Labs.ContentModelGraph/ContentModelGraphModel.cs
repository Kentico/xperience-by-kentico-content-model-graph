namespace Kentico.Xperience.Labs.ContentModelGraph;

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

    public string Kind { get; set; } = string.Empty;

    public string? SystemObjectTypeGroup { get; set; }

    public int FieldCount { get; set; }
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
}
