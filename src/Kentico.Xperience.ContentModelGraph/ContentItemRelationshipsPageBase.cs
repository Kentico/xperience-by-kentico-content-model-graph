using CMS.ContentEngine.Internal;
using CMS.DataEngine;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.UIPages;

namespace Kentico.Xperience.ContentModelGraph;

/// <summary>
/// Shared implementation for content relationship graph pages across sections (content hub, web pages,
/// emails, headless items). Subclasses only need to resolve the content item/language context of the
/// page they are hosted on; permission checks and the expand command are handled here.
/// </summary>
internal abstract class ContentItemRelationshipsPageBase<TClientProperties>(
    IContentItemRelationshipGraphBuilder builder,
    IWorkspacePermissionEvaluator workspacePermissionEvaluator,
    IInfoProvider<ContentItemInfo> contentItemInfoProvider)
    : Page<TClientProperties>
    where TClientProperties : ContentItemRelationshipsClientPropertiesBase, new()
{
    /// <summary>
    /// Resolves the content item identifier of the item currently being viewed.
    /// </summary>
    protected abstract Task<int> ResolveContentItemId();

    /// <summary>
    /// Resolves the content language identifier of the language currently being viewed.
    /// </summary>
    protected abstract Task<int> ResolveContentLanguageId();

    public override async Task<TClientProperties> ConfigureTemplateProperties(TClientProperties properties)
    {
        int contentItemId = await ResolveContentItemId();

        await CheckPermission(contentItemId);

        properties.Graph = await builder.Build(contentItemId, await ResolveContentLanguageId());

        return properties;
    }

    [PageCommand]
    public async Task<ICommandResponse<ContentItemRelationshipGraph>> ExpandRelationships(
        ExpandContentItemRelationshipsCommandArguments arguments)
    {
        await CheckPermission(arguments.ItemId);

        return ResponseFrom(await builder.Build(arguments.ItemId, await ResolveContentLanguageId()));
    }

    // Applies regardless of the item's section (page, reusable, email, headless), since every content
    // item belongs to a workspace whose View permission governs visibility in the Content Hub app.
    private async Task CheckPermission(int contentItemId)
    {
        var item = (await contentItemInfoProvider.Get()
            .Columns(nameof(ContentItemInfo.ContentItemWorkspaceID))
            .WhereEquals(nameof(ContentItemInfo.ContentItemID), contentItemId)
            .GetEnumerableTypedResultAsync())
            .FirstOrDefault();

        if (item is null || !(await workspacePermissionEvaluator.Evaluate(
            "View",
            typeof(ContentHubApplication),
            item.ContentItemWorkspaceID)).Succeeded)
        {
            throw new ForbiddenAccessException();
        }
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
