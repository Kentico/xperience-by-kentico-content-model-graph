using CMS.ContentEngine;
using CMS.ContentEngine.Internal;
using CMS.DataEngine;
using CMS.Websites.Internal;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Websites;
using Kentico.Xperience.Admin.Websites.UIPages;

[assembly: UIPage(
    typeof(WebPageLayout),
    "relationships",
    typeof(Kentico.Xperience.ContentModelGraph.WebPageRelationshipsPage),
    "Content relationships",
    "@kentico/xperience-content-model-graph/ContentItemRelationships",
    200,
    Icon = Icons.ChoiceMultiScheme)]

namespace Kentico.Xperience.ContentModelGraph;

internal sealed class WebPageRelationshipsPage(
    IContentItemRelationshipGraphBuilder builder,
    IWorkspacePermissionEvaluator workspacePermissionEvaluator,
    IInfoProvider<ContentItemInfo> contentItemInfoProvider,
    IInfoProvider<WebPageItemInfo> webPageItemInfoProvider,
    IInfoProvider<ContentLanguageInfo> contentLanguageInfoProvider)
    : ContentItemRelationshipsPageBase<WebPageRelationshipsClientProperties>(
        builder, workspacePermissionEvaluator, contentItemInfoProvider)
{
    [PageParameter(typeof(WebPageUrlIdentifierPageModelBinder), typeof(WebPageLayout))]
    public WebPageUrlIdentifier WebPageIdentifier { get; set; } = new(string.Empty, 0);

    protected override async Task<int> ResolveContentItemId()
    {
        var webPage = (await webPageItemInfoProvider.Get()
            .Columns(nameof(WebPageItemInfo.WebPageItemContentItemID))
            .WhereEquals(nameof(WebPageItemInfo.WebPageItemID), WebPageIdentifier.WebPageItemID)
            .GetEnumerableTypedResultAsync())
            .FirstOrDefault();

        return webPage?.WebPageItemContentItemID
            ?? throw new InvalidOperationException($"Web page {WebPageIdentifier.WebPageItemID} was not found.");
    }

    protected override async Task<int> ResolveContentLanguageId()
    {
        var language = (await contentLanguageInfoProvider.Get()
            .Columns(nameof(ContentLanguageInfo.ContentLanguageID))
            .WhereEquals(nameof(ContentLanguageInfo.ContentLanguageName), WebPageIdentifier.LanguageName)
            .GetEnumerableTypedResultAsync())
            .FirstOrDefault();

        return language?.ContentLanguageID
            ?? throw new InvalidOperationException($"Content language '{WebPageIdentifier.LanguageName}' was not found.");
    }
}

internal sealed class WebPageRelationshipsClientProperties : ContentItemRelationshipsClientPropertiesBase;
