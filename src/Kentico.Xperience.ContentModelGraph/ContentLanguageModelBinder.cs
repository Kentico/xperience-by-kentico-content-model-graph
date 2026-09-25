using CMS.ContentEngine;
using CMS.Core;
using CMS.DataEngine;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.UIPages;

namespace Kentico.Xperience.ContentModelGraph;

/// <summary>
/// Binds URL parameters into <see cref="ContentLanguageUrlIdentifier"/> properties.
/// </summary>
/// <remarks>
/// The platform ships an equivalent <c>Kentico.Xperience.Admin.Base.UIPages.ContentLanguageModelBinder</c>,
/// but it is <c>internal</c> to <c>Kentico.Xperience.Admin.Base</c> (verified against 31.7.3: referencing it
/// yields CS0122), so it cannot be used from this assembly. This copy exists only for that reason.
/// Note that it shadows the platform type by simple name in files that also have
/// <c>using Kentico.Xperience.Admin.Base.UIPages;</c>.
/// </remarks>
internal sealed class ContentLanguageModelBinder(string parameterName)
    : PageModelBinder<ContentLanguageUrlIdentifier>(parameterName)
{
    public override async Task<ContentLanguageUrlIdentifier> Bind(PageRouteValues routeValues)
    {
        if (!routeValues.TryGet(parameterName, out string languageName))
        {
            return new();
        }

        var language = (await Service.Resolve<IInfoProvider<ContentLanguageInfo>>().Get()
            .Columns(
                nameof(ContentLanguageInfo.ContentLanguageID),
                nameof(ContentLanguageInfo.ContentLanguageName))
            .WhereEquals(nameof(ContentLanguageInfo.ContentLanguageName), languageName)
            .GetEnumerableTypedResultAsync())
            .FirstOrDefault();

        return language is null
            ? new ContentLanguageUrlIdentifier { LanguageName = languageName }
            : new ContentLanguageUrlIdentifier
            {
                ContentLanguageID = language.ContentLanguageID,
                LanguageName = language.ContentLanguageName
            };
    }
}
