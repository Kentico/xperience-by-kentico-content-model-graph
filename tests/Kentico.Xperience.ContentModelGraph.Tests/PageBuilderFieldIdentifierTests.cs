namespace Kentico.Xperience.ContentModelGraph.Tests;

/// <summary>
/// Covers reading the builder <c>fieldIdentifiers</c> objects - the property name to reference group GUID map
/// that widget variants, sections and page templates each carry.
/// </summary>
public class PageBuilderFieldIdentifierTests
{
    private const string DEFAULT_VARIANT_IMAGE_GROUP = "b27ccf6b-cd44-45cb-b087-5021dca20a55";
    private const string SAMPLE_REQUESTS_IMAGE_GROUP = "8eab4cbc-e7b7-4859-9809-da9e3ec0eddf";
    private const string SECTION_THEME_GROUP = "c269123d-c8f7-4297-82a5-25c60fb72d52";
    private const string TEMPLATE_IMAGES_GROUP = "0ab07d28-484f-4d95-a11f-5b9e806151ae";

    private const string WIDGETS_JSON = $$"""
        {
          "editableAreas": [
            {
              "identifier": "top",
              "sections": [
                {
                  "type": "DancingGoat.SingleColumnSection",
                  "properties": { "theme": "section-white" },
                  "fieldIdentifiers": { "theme": "{{SECTION_THEME_GROUP}}" },
                  "zones": [
                    {
                      "widgets": [
                        {
                          "type": "DancingGoat.LandingPage.HeroImage",
                          "variants": [
                            {
                              "properties": { "image": [], "text": "Sign up" },
                              "fieldIdentifiers": {
                                "image": "{{DEFAULT_VARIANT_IMAGE_GROUP}}",
                                "text": "8e1f70c8-f291-4d6f-83ad-600b117f3510"
                              }
                            },
                            {
                              "name": "Sample Requests",
                              "properties": { "image": [] },
                              "conditionTypeParameters": { "variantName": null },
                              "fieldIdentifiers": { "image": "{{SAMPLE_REQUESTS_IMAGE_GROUP}}" }
                            }
                          ]
                        }
                      ]
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

    private const string TEMPLATE_JSON = $$"""
        {
          "identifier": "DancingGoat.LandingPageSingleColumn",
          "properties": { "images": [] },
          "fieldIdentifiers": { "images": "{{TEMPLATE_IMAGES_GROUP}}" }
        }
        """;

    private static List<PageBuilderFieldIdentifier> Read(string? widgetsJson, string? templateJson = null) =>
        [.. WidgetReferenceReader.ReadPageBuilderReferences(widgetsJson, templateJson).FieldIdentifiers];

    [Test]
    public void FieldIdentifiers_WidgetVariantEntryCarriesTheBuilderPath()
    {
        var entry = Read(WIDGETS_JSON).Single(entry => entry.ReferenceGroup == Guid.Parse(DEFAULT_VARIANT_IMAGE_GROUP));

        Assert.Multiple(() =>
        {
            Assert.That(entry.PropertyName, Is.EqualTo("image"));
            Assert.That(entry.Path.SourceKind, Is.EqualTo(PageBuilderSourceKind.Widget));
            Assert.That(entry.Path.TypeIdentifier, Is.EqualTo("DancingGoat.LandingPage.HeroImage"));
            Assert.That(entry.Path.AreaIdentifier, Is.EqualTo("top"));
            Assert.That(entry.Path.SectionTypeIdentifier, Is.EqualTo("DancingGoat.SingleColumnSection"));
            Assert.That(entry.Path.IsPersonalizationVariant, Is.False);
        });
    }

    // Every property is listed, not only the ones holding a reference; the GUID is looked up, never enumerated.
    [Test]
    public void FieldIdentifiers_EveryPropertyOfTheVariantIsRead()
    {
        var names = Read(WIDGETS_JSON)
            .Where(entry => entry.Path.SourceKind == PageBuilderSourceKind.Widget && !entry.Path.IsPersonalizationVariant)
            .Select(entry => entry.PropertyName);

        Assert.That(names, Is.EqualTo(new[] { "image", "text" }));
    }

    // Each personalization variant has its own GUID for the same property, so each variant's rows are told apart.
    [Test]
    public void FieldIdentifiers_PersonalizationVariantHasItsOwnGroup()
    {
        var entry = Read(WIDGETS_JSON).Single(entry => entry.ReferenceGroup == Guid.Parse(SAMPLE_REQUESTS_IMAGE_GROUP));

        Assert.Multiple(() =>
        {
            Assert.That(entry.PropertyName, Is.EqualTo("image"));
            Assert.That(entry.Path.VariantName, Is.EqualTo("Sample Requests"));
            Assert.That(entry.Path.IsPersonalizationVariant, Is.True);
        });
    }

    [Test]
    public void FieldIdentifiers_SectionEntryIsRead()
    {
        var entry = Read(WIDGETS_JSON).Single(entry => entry.ReferenceGroup == Guid.Parse(SECTION_THEME_GROUP));

        Assert.Multiple(() =>
        {
            Assert.That(entry.PropertyName, Is.EqualTo("theme"));
            Assert.That(entry.Path.SourceKind, Is.EqualTo(PageBuilderSourceKind.Section));
            Assert.That(entry.Path.TypeIdentifier, Is.EqualTo("DancingGoat.SingleColumnSection"));
            Assert.That(entry.Path.AreaIdentifier, Is.EqualTo("top"));
        });
    }

    [Test]
    public void FieldIdentifiers_TemplateEntryIsRead()
    {
        var entry = Read(null, TEMPLATE_JSON).Single();

        Assert.Multiple(() =>
        {
            Assert.That(entry.ReferenceGroup, Is.EqualTo(Guid.Parse(TEMPLATE_IMAGES_GROUP)));
            Assert.That(entry.PropertyName, Is.EqualTo("images"));
            Assert.That(entry.Path.SourceKind, Is.EqualTo(PageBuilderSourceKind.Template));
            Assert.That(entry.Path.TypeIdentifier, Is.EqualTo("DancingGoat.LandingPageSingleColumn"));
        });
    }

    // A variant can list its field identifiers with no properties object at all, and the entry is still read.
    [Test]
    public void FieldIdentifiers_AreReadWithoutAPropertiesObject()
    {
        string templateJson = $$"""
            { "identifier": "Template", "fieldIdentifiers": { "images": "{{TEMPLATE_IMAGES_GROUP}}" } }
            """;

        Assert.That(Read(null, templateJson), Has.Count.EqualTo(1));
    }

    [TestCase("""{ "identifier": "Template", "properties": {} }""")]
    [TestCase("""{ "identifier": "Template", "fieldIdentifiers": null }""")]
    [TestCase("""{ "identifier": "Template", "fieldIdentifiers": [] }""")]
    [TestCase("""{ "identifier": "Template", "fieldIdentifiers": "0ab07d28-484f-4d95-a11f-5b9e806151ae" }""")]
    [TestCase("""{ "identifier": "Template", "fieldIdentifiers": { "images": "not a guid" } }""")]
    [TestCase("""{ "identifier": "Template", "fieldIdentifiers": { "images": null } }""")]
    [TestCase("""{ "identifier": "Template", "fieldIdentifiers": { "images": 42 } }""")]
    [TestCase("""{ "identifier": "Template", "fieldIdentifiers": { "images": "00000000-0000-0000-0000-000000000000" } }""")]
    [TestCase("""{ "identifier": "Template", "fieldIdentifiers": { "images": """)]
    public void FieldIdentifiers_MissingOrMalformedEntriesAreSkipped(string templateJson)
    {
        List<PageBuilderFieldIdentifier> entries = [];

        Assert.DoesNotThrow(() => entries = Read(null, templateJson));
        Assert.That(entries, Is.Empty);
    }

    [Test]
    public void FieldIdentifiers_MalformedEntryDoesNotHideItsSiblings()
    {
        string templateJson = $$"""
            {
              "identifier": "Template",
              "fieldIdentifiers": { "broken": "not a guid", "images": "{{TEMPLATE_IMAGES_GROUP}}" }
            }
            """;

        Assert.That(Read(null, templateJson).Select(entry => entry.PropertyName), Is.EqualTo(new[] { "images" }));
    }
}
