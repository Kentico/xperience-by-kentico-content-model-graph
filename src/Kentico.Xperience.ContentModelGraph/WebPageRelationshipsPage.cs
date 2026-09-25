using CMS.ContentEngine;
using CMS.DataEngine;
using CMS.Websites.Internal;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Websites;
using Kentico.Xperience.Admin.Websites.UIPages;

[assembly: UIPage(
    typeof(WebPageLayout),
    Kentico.Xperience.ContentModelGraph.ContentModelGraphSlugs.Relationships,
    typeof(Kentico.Xperience.ContentModelGraph.WebPageRelationshipsPage),
    "Content relationships",
    "@kentico/xperience-content-model-graph/ContentItemRelationships",
    Kentico.Xperience.ContentModelGraph.ContentModelGraphPageOrder.AfterWebPageLayoutTabs,
    Icon = Icons.ProjectScheme)]

namespace Kentico.Xperience.ContentModelGraph;

internal sealed class WebPageRelationshipsPage(
    IContentItemRelationshipGraphBuilder builder,
    IContentItemGraphPermissionEvaluator permissionEvaluator,
    IInfoProvider<WebPageItemInfo> webPageItemInfoProvider,
    IInfoProvider<ContentLanguageInfo> contentLanguageInfoProvider)
    : ContentItemRelationshipsPageBase<WebPageRelationshipsClientProperties>(builder, permissionEvaluator)
{
    [PageParameter(typeof(WebPageUrlIdentifierPageModelBinder), typeof(WebPageLayout))]
    public WebPageUrlIdentifier WebPageIdentifier { get; set; } = new(string.Empty, 0);

    protected override async Task<int?> ResolveContentItemId()
    {
        var webPage = (await webPageItemInfoProvider.Get()
            .Columns(nameof(WebPageItemInfo.WebPageItemContentItemID))
            .WhereEquals(nameof(WebPageItemInfo.WebPageItemID), WebPageIdentifier.WebPageItemID)
            .GetEnumerableTypedResultAsync())
            .FirstOrDefault();

        return webPage?.WebPageItemContentItemID;
    }

    protected override async Task<int?> ResolveContentLanguageId()
    {
        var language = (await contentLanguageInfoProvider.Get()
            .Columns(nameof(ContentLanguageInfo.ContentLanguageID))
            .WhereEquals(nameof(ContentLanguageInfo.ContentLanguageName), WebPageIdentifier.LanguageName)
            .GetEnumerableTypedResultAsync())
            .FirstOrDefault();

        return language?.ContentLanguageID;
    }
}

internal sealed class WebPageRelationshipsClientProperties : ContentItemRelationshipsClientPropertiesBase;
