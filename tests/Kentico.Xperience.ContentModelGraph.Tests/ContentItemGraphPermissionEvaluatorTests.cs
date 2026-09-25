using System.Reflection;

using CMS.Websites.Internal;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.UIPages;
using Kentico.Xperience.Admin.DigitalMarketing.UIPages;
using Kentico.Xperience.Admin.Headless.UIPages;

namespace Kentico.Xperience.ContentModelGraph.Tests;

public class ContentItemGraphPermissionEvaluatorTests
{
    // The per-channel application name convention is read from the platform's internal registrations rather
    // than a documented contract, so these guard the assumption the whole channel permission check rests on.
    // If an upgrade renames an application, the check would otherwise silently deny every channel item.
    [Test]
    public void ChannelApplicationIdentifiers_MatchTheRecordedPlatformValues() => Assert.Multiple(() =>
                                                                                       {
                                                                                           Assert.That(
                                                                                               WebsiteConstants.WEBSITE_CHANNEL_APPLICATION_PREFIX,
                                                                                               Is.EqualTo(ContentItemGraphPermissionEvaluatorConstants.WebsiteChannelApplicationPrefix));
                                                                                           Assert.That(
                                                                                               EmailChannelApplication.IDENTIFIER,
                                                                                               Is.EqualTo(ContentItemGraphPermissionEvaluatorConstants.EmailChannelApplicationIdentifier));
                                                                                           Assert.That(
                                                                                               HeadlessChannelApplication.IDENTIFIER,
                                                                                               Is.EqualTo(ContentItemGraphPermissionEvaluatorConstants.HeadlessChannelApplicationIdentifier));
                                                                                       });

    [Test]
    public void GetChannelApplicationName_AppendsTheChannelGuidToTheIdentifier()
    {
        var channelGuid = new Guid("2a2e5f19-9e0b-4d06-9a4e-2a07d67bbd0d");

        string name = ContentItemGraphPermissionEvaluator.GetChannelApplicationName(
            WebsiteConstants.WEBSITE_CHANNEL_APPLICATION_PREFIX,
            channelGuid);

        Assert.That(name, Is.EqualTo($"{WebsiteConstants.WEBSITE_CHANNEL_APPLICATION_PREFIX}_2a2e5f19-9e0b-4d06-9a4e-2a07d67bbd0d"));
    }

    // The four applications the graphs link out to are static, so their identifiers are plain constants. They
    // are pinned here for the same reason as the channel prefixes above: an upgrade that renames one would
    // otherwise silently drop every content type, tag, form and object type link instead of failing a test.
    // Compiling at all is half the point - Kentico's XML documentation covers internal types too, so only a
    // reference that builds proves the type and its constant are usable from outside the platform.
    [Test]
    public void ContentTypesApplicationIdentifier_MatchesTheRecordedPlatformValue() =>
        Assert.That(
            ContentTypesApplication.IDENTIFIER,
            Is.EqualTo(ContentItemGraphPermissionEvaluatorConstants.ContentTypesApplicationIdentifier));

    [Test]
    public void TaxonomyApplicationIdentifier_MatchesTheRecordedPlatformValue() =>
        Assert.That(
            TaxonomyApplication.IDENTIFIER,
            Is.EqualTo(ContentItemGraphPermissionEvaluatorConstants.TaxonomyApplicationIdentifier));

    [Test]
    public void FormsApplicationIdentifier_MatchesTheRecordedPlatformValue() =>
        Assert.That(
            FormsApplication.IDENTIFIER,
            Is.EqualTo(ContentItemGraphPermissionEvaluatorConstants.FormsApplicationIdentifier));

    [Test]
    public void ModulesApplicationIdentifier_MatchesTheRecordedPlatformValue() =>
        Assert.That(
            ModulesApplication.IDENTIFIER,
            Is.EqualTo(ContentItemGraphPermissionEvaluatorConstants.ModulesApplicationIdentifier));

    // The content model graph links to content types, reusable field schemas, taxonomies and - for every
    // class that is neither a content type nor a form - module class definitions. Reusable field schemas
    // have no application of their own - the platform registers their pages under the Content types
    // application - so the same identifier governs them as governs content types. Rather than assume that,
    // this walks the page registrations the way the administration does. An upgrade that moved any of these
    // pages under a different application fails here, instead of leaving the graph suppressing links by a
    // permission that no longer governs them.
    [TestCase(typeof(ContentTypeFields), ContentItemGraphPermissionEvaluatorConstants.ContentTypesApplicationIdentifier)]
    [TestCase(typeof(ReusableFieldSchemaFields), ContentItemGraphPermissionEvaluatorConstants.ContentTypesApplicationIdentifier)]
    [TestCase(typeof(TaxonomyEdit), ContentItemGraphPermissionEvaluatorConstants.TaxonomyApplicationIdentifier)]
    [TestCase(typeof(ClassFields), ContentItemGraphPermissionEvaluatorConstants.ModulesApplicationIdentifier)]
    [TestCase(typeof(FormBuilderTab), ContentItemGraphPermissionEvaluatorConstants.FormsApplicationIdentifier)]
    public void LinkedAdministrationPage_IsGovernedByTheExpectedApplication(Type pageType, string expectedIdentifier) =>
        Assert.That(GetGoverningApplicationIdentifier(pageType), Is.EqualTo(expectedIdentifier));

    /// <summary>
    /// The application a page belongs to, found by walking its <c>UIPage</c> registrations up to the
    /// <c>UIApplication</c> at the root of the chain. Both administration assemblies the graphs link into
    /// are read, because the form builder is registered in Digital Marketing while everything else is in
    /// Base, and a chain may cross from one to the other.
    /// </summary>
    private static string? GetGoverningApplicationIdentifier(Type pageType)
    {
        Assembly[] assemblies = [typeof(ContentTypesApplication).Assembly, typeof(FormsApplication).Assembly];
        var pages = assemblies.SelectMany(assembly => assembly.GetCustomAttributes<UIPageAttribute>()).ToArray();
        var applications = assemblies.SelectMany(assembly => assembly.GetCustomAttributes<UIApplicationAttribute>()).ToArray();
        var current = pageType;

        // The chains here are a handful of pages deep; the bound only keeps a malformed one from hanging.
        for (int depth = 0; current is not null && depth < 20; depth++)
        {
            var application = Array.Find(applications, registration => registration.Type == current);
            if (application is not null)
            {
                return application.Identifier;
            }

            current = Array.Find(pages, registration => registration.Type == current)?.ParentType;
        }

        return null;
    }

    // An administrator bypasses the four checks, so the value they get has to open everything.
    [Test]
    public void GraphApplicationAccess_All_OpensEveryLinkedApplication()
    {
        var access = GraphApplicationAccess.All;

        Assert.Multiple(() =>
        {
            Assert.That(access.ContentTypes, Is.True);
            Assert.That(access.Taxonomy, Is.True);
            Assert.That(access.Forms, Is.True);
            Assert.That(access.Modules, Is.True);
        });
    }

    // Dependencies are deliberately null - an empty request must short-circuit before touching the database.
    [Test]
    public async Task GetViewableItemIds_WithNoItems_ReturnsEmptyWithoutQuerying()
    {
        var evaluator = new ContentItemGraphPermissionEvaluator(
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);

        var viewable = await evaluator.GetViewableItemIds([]);

        Assert.That(viewable, Is.Empty);
    }
}
