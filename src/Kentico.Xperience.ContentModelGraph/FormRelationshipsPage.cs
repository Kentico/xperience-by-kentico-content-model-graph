using CMS.ContentEngine;
using CMS.DataEngine;
using CMS.OnlineForms;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.DigitalMarketing.UIPages;

[assembly: UIPage(
    typeof(FormEditSection),
    Kentico.Xperience.ContentModelGraph.ContentModelGraphSlugs.Relationships,
    typeof(Kentico.Xperience.ContentModelGraph.FormRelationshipsPage),
    "Content relationships",
    "@kentico/xperience-content-model-graph/ContentItemRelationships",
    Kentico.Xperience.ContentModelGraph.ContentModelGraphPageOrder.BeforeLast,
    Icon = Icons.ProjectScheme)]

namespace Kentico.Xperience.ContentModelGraph;

/// <summary>
/// The relationship graph of a form: the form itself, plus the pages and emails whose Page Builder
/// configuration embeds it.
/// </summary>
/// <remarks>
/// Deliberately not a <see cref="ContentItemRelationshipsPageBase{TClientProperties}" />. That base is built
/// around the content item the page is hosted on - it validates the page against one, checks permissions
/// against one, and roots the graph at one. A form is not a content item: it has no content item identifier,
/// no language variant and no workspace or channel to evaluate permissions against, and the Forms application
/// governs access to this page instead. What the two pages genuinely share - expanding a neighbouring content
/// item, and the permission check that guards it - lives in
/// <see cref="RelationshipGraphPageBase{TClientProperties}" />, which both derive from.
/// </remarks>
internal sealed class FormRelationshipsPage(
    IContentItemRelationshipGraphBuilder builder,
    IContentItemGraphPermissionEvaluator permissionEvaluator,
    IInfoProvider<BizFormInfo> bizFormInfoProvider,
    IInfoProvider<ContentLanguageInfo> contentLanguageInfoProvider)
    : RelationshipGraphPageBase<FormRelationshipsClientProperties>(builder, permissionEvaluator)
{
    private Task<bool>? formExists;
    private Task<int?>? defaultLanguageId;

    [PageParameter(typeof(IntPageModelBinder), typeof(FormEditSection))]
    public int ObjectId { get; set; }

    public override async Task<PageValidationResult> ValidatePage() =>
        new()
        {
            IsValid = await GetFormExists(),
            ErrorMessageKey = "base.forms.error.objectnotinitialized"
        };

    public override async Task<FormRelationshipsClientProperties> ConfigureTemplateProperties(
        FormRelationshipsClientProperties properties)
    {
        // Unreachable in the page load path - ValidatePage short-circuits when the form does not exist.
        if (!await GetFormExists())
        {
            return properties;
        }

        properties.Graph = await Builder.BuildForForm(ObjectId);

        return properties;
    }

    // The Forms application has no language switcher, so expanding a page reached from this graph reads it in
    // the default content language - the same language the graph itself was built in.
    protected override Task<int?> ResolveExpansionLanguageId() => GetDefaultLanguageId();

    private Task<bool> GetFormExists() => formExists ??= ResolveFormExists();

    private async Task<bool> ResolveFormExists() =>
        (await bizFormInfoProvider.Get()
            .Columns(nameof(BizFormInfo.FormID))
            .WhereEquals(nameof(BizFormInfo.FormID), ObjectId)
            .GetEnumerableTypedResultAsync())
            .Any();

    private Task<int?> GetDefaultLanguageId() => defaultLanguageId ??= ResolveDefaultLanguageId();

    private async Task<int?> ResolveDefaultLanguageId()
    {
        var languages = (await contentLanguageInfoProvider.Get()
            .Columns(
                nameof(ContentLanguageInfo.ContentLanguageID),
                nameof(ContentLanguageInfo.ContentLanguageIsDefault))
            .GetEnumerableTypedResultAsync())
            .ToList();

        return (languages.FirstOrDefault(language => language.ContentLanguageIsDefault)
            ?? languages.OrderBy(language => language.ContentLanguageID).FirstOrDefault())
            ?.ContentLanguageID;
    }
}

internal sealed class FormRelationshipsClientProperties : ContentItemRelationshipsClientPropertiesBase;
