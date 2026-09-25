using Kentico.Xperience.Admin.Base;

namespace Kentico.Xperience.ContentModelGraph;

/// <summary>
/// Shared implementation for every relationship graph page, whatever the graph is rooted at. Owns the
/// expand command and the permission check it needs, because expansion always walks from one content item
/// to its neighbours even when the root of the graph is not a content item at all (a form, for instance).
/// </summary>
internal abstract class RelationshipGraphPageBase<TClientProperties>(
    IContentItemRelationshipGraphBuilder builder,
    IContentItemGraphPermissionEvaluator permissionEvaluator)
    : Page<TClientProperties>
    where TClientProperties : ContentItemRelationshipsClientPropertiesBase, new()
{
    protected IContentItemRelationshipGraphBuilder Builder { get; } = builder;

    /// <summary>
    /// The content language expansion reads related items in. Pages hosted on a language-scoped section
    /// return the language being edited; pages without one return a sensible default.
    /// </summary>
    protected abstract Task<int?> ResolveExpansionLanguageId();

    [PageCommand]
    public async Task<ICommandResponse<ContentItemRelationshipGraph>> ExpandRelationships(
        ExpandContentItemRelationshipsCommandArguments arguments)
    {
        await CheckPermission(arguments.ItemId);

        int languageId = await ResolveExpansionLanguageId()
            ?? throw new InvalidOperationException("The content language of the current page was not found.");

        return ResponseFrom(await Builder.Build(arguments.ItemId, languageId));
    }

    // The identifier reaching ExpandRelationships comes from the client, so it is checked against the
    // surface the item actually lives on (workspace for reusable items, channel application plus web page
    // ACLs for channel items) rather than against the page the request happens to be hosted on.
    protected async Task CheckPermission(int contentItemId)
    {
        if (!(await permissionEvaluator.GetViewableItemIds([contentItemId])).Contains(contentItemId))
        {
            throw new ForbiddenAccessException();
        }
    }
}

/// <summary>
/// Shared implementation for content relationship graph pages across sections (content hub, web pages,
/// emails, headless items). Subclasses only need to resolve the content item/language context of the
/// page they are hosted on; permission checks and the expand command are handled here.
/// </summary>
internal abstract class ContentItemRelationshipsPageBase<TClientProperties>(
    IContentItemRelationshipGraphBuilder builder,
    IContentItemGraphPermissionEvaluator permissionEvaluator)
    : RelationshipGraphPageBase<TClientProperties>(builder, permissionEvaluator)
    where TClientProperties : ContentItemRelationshipsClientPropertiesBase, new()
{
    // Both identifiers are resolved at most once per request (the page instance is created per request),
    // so ValidatePage and ConfigureTemplateProperties share a single lookup.
    private Task<int?>? contentItemId;
    private Task<int?>? contentLanguageId;

    /// <summary>
    /// Resolves the content item identifier of the item currently being viewed,
    /// or <see langword="null" /> when the URL does not point at an existing item.
    /// </summary>
    protected abstract Task<int?> ResolveContentItemId();

    /// <summary>
    /// Resolves the content language identifier of the language currently being viewed,
    /// or <see langword="null" /> when the URL does not point at an existing language.
    /// </summary>
    protected abstract Task<int?> ResolveContentLanguageId();

    private Task<int?> GetContentItemId() => contentItemId ??= ResolveContentItemId();

    private Task<int?> GetContentLanguageId() => contentLanguageId ??= ResolveContentLanguageId();

    protected override Task<int?> ResolveExpansionLanguageId() => GetContentLanguageId();

    // Called by the admin UI before ConfigurePage, so an unresolvable URL (eg the synthetic website channel
    // root, which has no content item behind it) renders the platform's "This object doesn't exist" screen
    // instead of failing with a server error.
    public override async Task<PageValidationResult> ValidatePage() =>
        new()
        {
            IsValid = await GetContentItemId() is not null && await GetContentLanguageId() is not null,
            ErrorMessageKey = "base.forms.error.objectnotinitialized"
        };

    public override async Task<TClientProperties> ConfigureTemplateProperties(TClientProperties properties)
    {
        // Unreachable in the page load path - ValidatePage short-circuits when either identifier is missing.
        if (await GetContentItemId() is not int itemId || await GetContentLanguageId() is not int languageId)
        {
            return properties;
        }

        await CheckPermission(itemId);

        properties.Graph = await Builder.Build(itemId, languageId);

        return properties;
    }
}

internal abstract class ContentItemRelationshipsClientPropertiesBase : TemplateClientProperties
{
    public ContentItemRelationshipGraph? Graph { get; set; }
}

internal sealed class ExpandContentItemRelationshipsCommandArguments
{
    public int ItemId { get; set; }
}
