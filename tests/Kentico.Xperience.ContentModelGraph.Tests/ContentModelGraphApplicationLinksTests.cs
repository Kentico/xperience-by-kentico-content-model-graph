namespace Kentico.Xperience.ContentModelGraph.Tests;

/// <summary>
/// The content model graph links out to the Content types application (content types and reusable field
/// schemas alike), the Taxonomy application, the Forms application (form classes) and the Modules
/// application (every other class). A user without access to one of them keeps the node's label and loses
/// its link, so these pin which access flag governs which link - and that a denied application never costs
/// a page link generation, of which this graph has one per node.
/// </summary>
public class ContentModelGraphApplicationLinksTests
{
    private static readonly GraphApplicationAccess contentTypesDenied =
        new(ContentTypes: false, Taxonomy: true, Forms: true, Modules: true);

    private static readonly GraphApplicationAccess taxonomyDenied =
        new(ContentTypes: true, Taxonomy: false, Forms: true, Modules: true);

    private static readonly GraphApplicationAccess formsDenied =
        new(ContentTypes: true, Taxonomy: true, Forms: false, Modules: true);

    private static readonly GraphApplicationAccess modulesDenied =
        new(ContentTypes: true, Taxonomy: true, Forms: true, Modules: false);

    /// <summary>The lookup found no form for any class - what a graph with no forms in it produces.</summary>
    private static readonly IReadOnlyDictionary<int, int> noForms = new Dictionary<int, int>();

    [Test]
    public void ForContentType_WithAccessToTheContentTypesApplication_KeepsTheLink() =>
        Assert.That(
            ContentModelGraphApplicationLinks.ForContentType(GraphApplicationAccess.All, () => "/admin/content-types/6665/fields"),
            Is.EqualTo("/admin/content-types/6665/fields"));

    [Test]
    public void ForContentType_WithoutAccessToTheContentTypesApplication_DropsTheLink() =>
        Assert.That(
            ContentModelGraphApplicationLinks.ForContentType(contentTypesDenied, () => "/admin/content-types/6665/fields"),
            Is.Null);

    // Reusable field schemas are pages of the Content types application, not an application of their own -
    // so the Content types flag governs them. Denying taxonomy alone must leave a schema link alone, and
    // denying content types must take it away.
    [Test]
    public void ForReusableFieldSchema_IsGovernedByTheContentTypesApplication() => Assert.Multiple(() =>
    {
        Assert.That(
            ContentModelGraphApplicationLinks.ForReusableFieldSchema(taxonomyDenied, () => "/admin/reusable-field-schemas/fields"),
            Is.EqualTo("/admin/reusable-field-schemas/fields"));
        Assert.That(
            ContentModelGraphApplicationLinks.ForReusableFieldSchema(contentTypesDenied, () => "/admin/reusable-field-schemas/fields"),
            Is.Null);
    });

    [Test]
    public void ForTaxonomy_IsGovernedByTheTaxonomyApplication() => Assert.Multiple(() =>
    {
        Assert.That(
            ContentModelGraphApplicationLinks.ForTaxonomy(contentTypesDenied, () => "/admin/taxonomy/12/tags"),
            Is.EqualTo("/admin/taxonomy/12/tags"));
        Assert.That(
            ContentModelGraphApplicationLinks.ForTaxonomy(taxonomyDenied, () => "/admin/taxonomy/12/tags"),
            Is.Null);
    });

    // A form class is authored in the Forms application, not in Modules - so denying Modules must leave the
    // form link alone, and denying Forms must take it away.
    [Test]
    public void ForForm_IsGovernedByTheFormsApplication() => Assert.Multiple(() =>
    {
        Assert.That(
            ContentModelGraphApplicationLinks.ForForm(modulesDenied, () => "/admin/forms/list/2/builder"),
            Is.EqualTo("/admin/forms/list/2/builder"));
        Assert.That(
            ContentModelGraphApplicationLinks.ForForm(formsDenied, () => "/admin/forms/list/2/builder"),
            Is.Null);
    });

    [Test]
    public void ForObjectType_IsGovernedByTheModulesApplication() => Assert.Multiple(() =>
    {
        Assert.That(
            ContentModelGraphApplicationLinks.ForObjectType(formsDenied, () => "/admin/modules/4/classes/9/fields"),
            Is.EqualTo("/admin/modules/4/classes/9/fields"));
        Assert.That(
            ContentModelGraphApplicationLinks.ForObjectType(modulesDenied, () => "/admin/modules/4/classes/9/fields"),
            Is.Null);
    });

    [Test]
    public void ForEveryLink_WhenTheApplicationIsNotAccessible_TheUrlIsNotBuilt()
    {
        int builds = 0;
        string Build()
        {
            builds++;

            return "/admin/content-types/6665/fields";
        }

        Assert.Multiple(() =>
        {
            Assert.That(ContentModelGraphApplicationLinks.ForContentType(contentTypesDenied, Build), Is.Null);
            Assert.That(ContentModelGraphApplicationLinks.ForReusableFieldSchema(contentTypesDenied, Build), Is.Null);
            Assert.That(ContentModelGraphApplicationLinks.ForTaxonomy(taxonomyDenied, Build), Is.Null);
            Assert.That(ContentModelGraphApplicationLinks.ForForm(formsDenied, Build), Is.Null);
            Assert.That(ContentModelGraphApplicationLinks.ForObjectType(modulesDenied, Build), Is.Null);
            Assert.That(builds, Is.Zero);
        });
    }

    // The page link generator is deliberately null: a denied application must not reach it at all, so these
    // would throw rather than return null if the builder asked for a URL it could not use.
    [Test]
    public void GetTaxonomyAdminUrl_WithoutTaxonomyAccess_ReturnsNoLinkWithoutGeneratingOne()
    {
        var builder = CreateBuilder();

        Assert.That(builder.GetTaxonomyAdminUrl(12, taxonomyDenied), Is.Null);
    }

    [Test]
    public void GetReusableFieldSchemaAdminUrl_WithoutContentTypesAccess_ReturnsNoLinkWithoutGeneratingOne()
    {
        var builder = CreateBuilder();

        Assert.That(
            builder.GetReusableFieldSchemaAdminUrl(new Guid("6bd76a2d-2c0e-4bb1-9f4c-9a1b3c5f8f7a"), contentTypesDenied),
            Is.Null);
    }

    [Test]
    public void GetClassAdminUrl_ForAnObjectTypeWithoutModulesAccess_ReturnsNoLinkWithoutGeneratingOne()
    {
        var builder = CreateBuilder();

        Assert.That(
            builder.GetClassAdminUrl(
                classId: 9,
                classResourceId: 4,
                classType: "Other",
                GraphNodeKind.OBJECT_TYPE,
                noForms,
                modulesDenied),
            Is.Null);
    }

    [Test]
    public void GetClassAdminUrl_ForAFormClassWithoutFormsAccess_ReturnsNoLinkWithoutGeneratingOne()
    {
        var builder = CreateBuilder();

        Assert.That(
            builder.GetClassAdminUrl(
                classId: 9,
                classResourceId: 4,
                classType: "Form",
                GraphNodeKind.FORMS,
                new Dictionary<int, int> { [9] = 2 },
                formsDenied),
            Is.Null);
    }

    // A form class whose form the ClassID -> FormID lookup did not find - a class left behind by a deleted
    // form, say. The Forms application has no address for it, so the node keeps its label and gets no link,
    // and in particular does not fall back to the Modules link it used to get.
    [Test]
    public void GetClassAdminUrl_ForAFormClassWithNoMatchingForm_ReturnsNoLinkWithoutGeneratingOne()
    {
        var builder = CreateBuilder();

        Assert.That(
            builder.GetClassAdminUrl(
                classId: 9,
                classResourceId: 4,
                classType: "Form",
                GraphNodeKind.FORMS,
                noForms,
                GraphApplicationAccess.All),
            Is.Null);
    }

    /// <summary>
    /// Every dependency null on purpose. A denied application, or a form class with no form, must return
    /// before it touches the page link generator or the form provider - reaching either throws here.
    /// </summary>
    private static ContentModelGraphBuilder CreateBuilder() => new(null!, null!, null!);
}
