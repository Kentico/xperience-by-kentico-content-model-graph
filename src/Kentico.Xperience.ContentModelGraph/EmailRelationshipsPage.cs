using CMS.DataEngine;
using CMS.EmailLibrary;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.UIPages;
using Kentico.Xperience.Admin.DigitalMarketing.UIPages;

[assembly: UIPage(
    typeof(EmailEditLayout),
    Kentico.Xperience.ContentModelGraph.ContentModelGraphSlugs.Relationships,
    typeof(Kentico.Xperience.ContentModelGraph.EmailRelationshipsPage),
    "Content relationships",
    "@kentico/xperience-content-model-graph/ContentItemRelationships",
    1001,
    Icon = Icons.ProjectScheme)]

namespace Kentico.Xperience.ContentModelGraph;

internal sealed class EmailRelationshipsPage(
    IContentItemRelationshipGraphBuilder builder,
    IContentItemGraphPermissionEvaluator permissionEvaluator,
    IInfoProvider<EmailConfigurationInfo> emailConfigurationInfoProvider)
    : ContentItemRelationshipsPageBase<EmailRelationshipsClientProperties>(builder, permissionEvaluator)
{
    [PageParameter(typeof(IntPageModelBinder), typeof(EmailEditLayout))]
    public int EmailConfigurationID { get; set; }

    [PageParameter(typeof(ContentLanguageModelBinder), typeof(EmailChannelContentLanguage))]
    public ContentLanguageUrlIdentifier ContentLanguageIdentifier { get; set; } = new();

    protected override async Task<int?> ResolveContentItemId()
    {
        var emailConfiguration = (await emailConfigurationInfoProvider.Get()
            .Columns(nameof(EmailConfigurationInfo.EmailConfigurationContentItemID))
            .WhereEquals(nameof(EmailConfigurationInfo.EmailConfigurationID), EmailConfigurationID)
            .GetEnumerableTypedResultAsync())
            .FirstOrDefault();

        return emailConfiguration?.EmailConfigurationContentItemID;
    }

    protected override Task<int?> ResolveContentLanguageId() =>
        Task.FromResult<int?>(ContentLanguageIdentifier.ContentLanguageID);
}

internal sealed class EmailRelationshipsClientProperties : ContentItemRelationshipsClientPropertiesBase;
