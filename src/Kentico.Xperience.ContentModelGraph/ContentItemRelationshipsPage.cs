using CMS.ContentEngine.Internal;
using CMS.DataEngine;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.UIPages;

[assembly: UIPage(
    typeof(ContentItemEditSection),
    "relationships",
    typeof(Kentico.Xperience.ContentModelGraph.ContentItemRelationshipsPage),
    "Content relationships",
    "@kentico/xperience-content-model-graph/ContentItemRelationships",
    200,
    Icon = Icons.ChoiceMultiScheme)]

namespace Kentico.Xperience.ContentModelGraph;

internal sealed class ContentItemRelationshipsPage(
    IContentItemRelationshipGraphBuilder builder,
    IWorkspacePermissionEvaluator workspacePermissionEvaluator,
    IInfoProvider<ContentItemInfo> contentItemInfoProvider)
    : ContentItemRelationshipsPageBase<ContentItemRelationshipsClientProperties>(
        builder, workspacePermissionEvaluator, contentItemInfoProvider)
{
    [PageParameter(typeof(IntPageModelBinder), typeof(ContentItemEditSection))]
    public int ItemID { get; set; }

    [PageParameter(typeof(ContentLanguageModelBinder), typeof(ContentHubContentLanguage))]
    public ContentLanguageUrlIdentifier ContentLanguageIdentifier { get; set; } = new();

    protected override Task<int> ResolveContentItemId() => Task.FromResult(ItemID);

    protected override Task<int> ResolveContentLanguageId() =>
        Task.FromResult(ContentLanguageIdentifier.ContentLanguageID);
}

internal sealed class ContentItemRelationshipsClientProperties : ContentItemRelationshipsClientPropertiesBase;
