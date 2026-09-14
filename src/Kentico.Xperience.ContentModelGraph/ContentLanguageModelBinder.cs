using CMS.ContentEngine;
using CMS.Core;
using CMS.DataEngine;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.UIPages;

namespace Kentico.Xperience.ContentModelGraph;

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
