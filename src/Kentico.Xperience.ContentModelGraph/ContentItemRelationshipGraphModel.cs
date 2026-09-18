namespace Kentico.Xperience.ContentModelGraph;

public sealed class ContentItemRelationshipGraph
{
    public required ContentItemRelationshipItem RootItem { get; init; }

    public required IReadOnlyCollection<ContentItemRelationship> Incoming { get; init; }

    public required IReadOnlyCollection<ContentItemRelationship> Outgoing { get; init; }
}

public sealed class ContentItemRelationship
{
    public required string Id { get; init; }

    public required ContentItemRelationshipItem RelatedItem { get; init; }

    public required string FieldLabel { get; init; }

    public required string FieldCodeName { get; init; }

    public required string Direction { get; init; }
}

public sealed class ContentItemRelationshipItem
{
    public int? ItemId { get; init; }

    public string? Identifier { get; init; }

    public required string DisplayName { get; init; }

    public required string CodeName { get; init; }

    public required string ContentTypeDisplayName { get; init; }

    public required string ContentTypeCodeName { get; init; }

    public string? ContentTypeAdminUrl { get; init; }

    public required string Kind { get; init; }

    public string? LocationName { get; init; }

    public string? LocationKind { get; init; }

    public string? AdminUrl { get; init; }

    public string? LiveUrl { get; init; }

    public bool IsDefaultLanguageFallback { get; init; }

    public string? FallbackLanguageCode { get; init; }
}
