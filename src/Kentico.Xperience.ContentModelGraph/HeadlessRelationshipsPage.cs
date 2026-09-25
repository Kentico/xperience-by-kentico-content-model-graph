using CMS.DataEngine;
using CMS.Headless.Internal;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.UIPages;
using Kentico.Xperience.Admin.Headless.UIPages;

[assembly: UIPage(
    typeof(HeadlessEditLayout),
    Kentico.Xperience.ContentModelGraph.ContentModelGraphSlugs.Relationships,
    typeof(Kentico.Xperience.ContentModelGraph.HeadlessRelationshipsPage),
    "Content relationships",
    "@kentico/xperience-content-model-graph/ContentItemRelationships",
    1001,
    Icon = Icons.ProjectScheme)]

namespace Kentico.Xperience.ContentModelGraph;

internal sealed class HeadlessRelationshipsPage(
    IContentItemRelationshipGraphBuilder builder,
    IContentItemGraphPermissionEvaluator permissionEvaluator,
    IInfoProvider<HeadlessItemInfo> headlessItemInfoProvider)
    : ContentItemRelationshipsPageBase<HeadlessRelationshipsClientProperties>(builder, permissionEvaluator)
{
    [PageParameter(typeof(IntPageModelBinder), typeof(HeadlessEditLayout))]
    public int HeadlessItemID { get; set; }

    [PageParameter(typeof(ContentLanguageModelBinder), typeof(HeadlessChannelContentLanguage))]
    public ContentLanguageUrlIdentifier ContentLanguageIdentifier { get; set; } = new();

    protected override async Task<int?> ResolveContentItemId()
    {
        var headlessItem = (await headlessItemInfoProvider.Get()
            .Columns(nameof(HeadlessItemInfo.HeadlessItemContentItemID))
            .WhereEquals(nameof(HeadlessItemInfo.HeadlessItemID), HeadlessItemID)
            .GetEnumerableTypedResultAsync())
            .FirstOrDefault();

        return headlessItem?.HeadlessItemContentItemID;
    }

    protected override Task<int?> ResolveContentLanguageId() =>
        Task.FromResult<int?>(ContentLanguageIdentifier.ContentLanguageID);
}

internal sealed class HeadlessRelationshipsClientProperties : ContentItemRelationshipsClientPropertiesBase;
