namespace Kentico.Xperience.ContentModelGraph;

public sealed class ContentItemRelationshipGraph
{
    public required ContentItemRelationshipItem RootItem { get; init; }

    public required IReadOnlyCollection<ContentItemRelationship> Incoming { get; init; }

    public required IReadOnlyCollection<ContentItemRelationship> Outgoing { get; init; }

    /// <summary>
    /// The code name of the content language the graph was requested in - what the viewer asked for, as opposed
    /// to <see cref="ContentItemRelationshipItem.LanguageCode" />, which records what each item actually
    /// resolved to and can differ through language fallback. A form's graph reports the default content
    /// language, which is the one its nodes were read in. Null when no language was ever resolved - a graph
    /// for an item or form that no longer exists, or a form nothing references.
    /// </summary>
    public string? LanguageCode { get; init; }

    /// <summary>
    /// One entry per direction that was capped before the graph was built, so the client can say that the
    /// view is incomplete instead of silently showing a subset. Empty when nothing was truncated.
    /// </summary>
    public IReadOnlyCollection<ContentItemRelationshipTruncation> Truncations { get; init; } = [];
}

/// <summary>
/// Reports that one direction of a graph shows only part of what exists. Counts are of related
/// <em>items</em>, not of relationships: one item can be reached by several edges (a form embedded by two
/// widgets on the same page is one item and two edges), and the cap is applied per item.
/// </summary>
public sealed class ContentItemRelationshipTruncation
{
    public required string Direction { get; init; }

    public required int ShownItemCount { get; init; }

    public required int TotalItemCount { get; init; }
}

public sealed class ContentItemRelationship
{
    public required string Id { get; init; }

    public required ContentItemRelationshipItem RelatedItem { get; init; }

    public required string FieldLabel { get; init; }

    public required string FieldCodeName { get; init; }

    /// <summary>
    /// For a reference stored in the Page Builder configuration, where it lives - editable area, section,
    /// widget, personalization variant and property - one line per occurrence. Null for every other source.
    /// </summary>
    public string? FieldPath { get; init; }

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

    /// <summary>
    /// The Content types application link for the node's content type. Null when the content type could not be
    /// resolved, and when the current user cannot open that application - the client then renders
    /// <see cref="ContentTypeDisplayName" /> as plain text, so the reader still learns what the item is.
    /// </summary>
    public string? ContentTypeAdminUrl { get; init; }

    public required string Kind { get; init; }

    public string? LocationName { get; init; }

    public string? LocationKind { get; init; }

    /// <summary>
    /// Where the node's own subject is edited. Null when there is nowhere to send the reader, when the item is
    /// <see cref="IsRestricted" />, and - for a taxonomy tag or a form node - when the current user cannot open
    /// the Taxonomy or Forms application. Only the link goes in that last case: the node keeps its name.
    /// </summary>
    public string? AdminUrl { get; init; }

    public string? LiveUrl { get; init; }

    public bool IsDefaultLanguageFallback { get; init; }

    /// <summary>
    /// The code name of the content language the item's metadata was actually read in, which is the requested
    /// language unless <see cref="IsDefaultLanguageFallback" /> says it fell back. Null for nodes that carry no
    /// language at all - a form, or an item with no language metadata row in the fallback chain.
    /// </summary>
    public string? LanguageCode { get; init; }

    /// <summary>
    /// Indicates that the content item no longer exists. The client keeps the metadata it already
    /// holds for the node so the broken reference stays diagnosable, and stops offering expansion.
    /// </summary>
    public bool IsMissing { get; init; }

    /// <summary>
    /// Indicates that the current user is not allowed to view the content item. The node stays in the graph so
    /// the relationship remains visible, matching Xperience's own Content hub "Used in" tab, which lists such an
    /// item in full and only disables its link. As that tab does, the node withholds what the user may not see:
    /// no administration or live link, no <see cref="CodeName" />, and a <see cref="LocationName" /> that names
    /// only the kind of place the item lives in rather than the workspace or channel. It cannot be expanded.
    /// </summary>
    public bool IsRestricted { get; init; }
}

/// <summary>
/// The generic kind of place a content item lives, spelled the way the admin UI spells it. Reported to the
/// client as <see cref="ContentItemRelationshipItem.LocationKind" />, and used to decide which locations may
/// still name themselves on a node for an item the current user may not read.
/// </summary>
internal static class ItemLocationKind
{
    public const string WEBSITE = "Website";
    public const string HEADLESS = "Headless";
    public const string EMAIL = "Email";
    public const string CONTENT_HUB = "Content hub";
}
