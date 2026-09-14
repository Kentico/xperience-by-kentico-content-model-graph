using CMS.ContentEngine.Internal;
using CMS.DataEngine;
using CMS.Headless.Internal;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.UIPages;
using Kentico.Xperience.Admin.Headless.UIPages;

[assembly: UIPage(
    typeof(HeadlessEditLayout),
    "relationships",
    typeof(Kentico.Xperience.ContentModelGraph.HeadlessRelationshipsPage),
    "Content relationships",
    "@kentico/xperience-content-model-graph/ContentItemRelationships",
    200,
    Icon = Icons.ChoiceMultiScheme)]

namespace Kentico.Xperience.ContentModelGraph;

internal sealed class HeadlessRelationshipsPage(
    IContentItemRelationshipGraphBuilder builder,
    IWorkspacePermissionEvaluator workspacePermissionEvaluator,
    IInfoProvider<ContentItemInfo> contentItemInfoProvider,
    IInfoProvider<HeadlessItemInfo> headlessItemInfoProvider)
    : ContentItemRelationshipsPageBase<HeadlessRelationshipsClientProperties>(
        builder, workspacePermissionEvaluator, contentItemInfoProvider)
{
    [PageParameter(typeof(IntPageModelBinder), typeof(HeadlessEditLayout))]
    public int HeadlessItemID { get; set; }

    [PageParameter(typeof(ContentLanguageModelBinder), typeof(HeadlessChannelContentLanguage))]
    public ContentLanguageUrlIdentifier ContentLanguageIdentifier { get; set; } = new();

    protected override async Task<int> ResolveContentItemId()
    {
        var headlessItem = (await headlessItemInfoProvider.Get()
            .Columns(nameof(HeadlessItemInfo.HeadlessItemContentItemID))
            .WhereEquals(nameof(HeadlessItemInfo.HeadlessItemID), HeadlessItemID)
            .GetEnumerableTypedResultAsync())
            .FirstOrDefault();

        return headlessItem?.HeadlessItemContentItemID
            ?? throw new InvalidOperationException($"Headless item {HeadlessItemID} was not found.");
    }

    protected override Task<int> ResolveContentLanguageId() =>
        Task.FromResult(ContentLanguageIdentifier.ContentLanguageID);
}

internal sealed class HeadlessRelationshipsClientProperties : ContentItemRelationshipsClientPropertiesBase;
