using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.UIPages;

[assembly: UIPage(
    typeof(ContentItemEditSection),
    Kentico.Xperience.ContentModelGraph.ContentModelGraphSlugs.Relationships,
    typeof(Kentico.Xperience.ContentModelGraph.ContentItemRelationshipsPage),
    "Content relationships",
    "@kentico/xperience-content-model-graph/ContentItemRelationships",
    1001,
    Icon = Icons.ProjectScheme)]

namespace Kentico.Xperience.ContentModelGraph;

internal sealed class ContentItemRelationshipsPage(
    IContentItemRelationshipGraphBuilder builder,
    IContentItemGraphPermissionEvaluator permissionEvaluator)
    : ContentItemRelationshipsPageBase<ContentItemRelationshipsClientProperties>(builder, permissionEvaluator)
{
    [PageParameter(typeof(IntPageModelBinder), typeof(ContentItemEditSection))]
    public int ItemID { get; set; }

    [PageParameter(typeof(ContentLanguageModelBinder), typeof(ContentHubContentLanguage))]
    public ContentLanguageUrlIdentifier ContentLanguageIdentifier { get; set; } = new();

    protected override Task<int?> ResolveContentItemId() => Task.FromResult<int?>(ItemID);

    protected override Task<int?> ResolveContentLanguageId() =>
        Task.FromResult<int?>(ContentLanguageIdentifier.ContentLanguageID);
}

internal sealed class ContentItemRelationshipsClientProperties : ContentItemRelationshipsClientPropertiesBase;
