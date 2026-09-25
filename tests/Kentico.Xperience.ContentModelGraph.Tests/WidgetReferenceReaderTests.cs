using System.Text.Json;

namespace Kentico.Xperience.ContentModelGraph.Tests;

public class WidgetReferenceReaderTests
{
    private const string HERO_IMAGE_IDENTIFIER = "8a528627-fc98-46a4-9b2f-c56b270e86c5";
    private const string FIRST_PRODUCT_IDENTIFIER = "cd3098ae-f4d3-409a-887a-3e4c6e0c856e";
    private const string SECOND_PRODUCT_IDENTIFIER = "a9689fb8-e06f-4363-9aa1-b5c1b427a0ee";
    private const string SECTION_IMAGE_IDENTIFIER = "b8117d96-dfb3-474d-ad27-c3b0d3bd26db";
    private const string TEMPLATE_IMAGE_IDENTIFIER = "0f4b2c7a-9c5e-4b6f-9a53-2e1d7a1c8f10";

    // Trimmed from a real Page Builder configuration, including its camel case property names, the
    // "type" key carrying the widget and section type identifiers, section level properties, and the
    // personalization variants of the hero image widget.
    private const string PAGE_BUILDER_JSON = $$"""
        {
          "editableAreas": [
            {
              "identifier": "top",
              "sections": [
                {
                  "identifier": "9b99f36a-f522-44d4-bfb9-fc075f1a3bc1",
                  "type": "DancingGoat.SingleColumnSection",
                  "properties": {
                    "theme": "section-white",
                    "background": [ { "identifier": "{{SECTION_IMAGE_IDENTIFIER}}" } ]
                  },
                  "zones": [
                    {
                      "identifier": "fa8a8e06-42a6-43c9-960f-cfee9949025b",
                      "widgets": [
                        {
                          "identifier": "63718636-7deb-4b9a-91c2-9f5f39c8f0fd",
                          "type": "DancingGoat.LandingPage.HeroImage",
                          "conditionType": "DancingGoat.Personalization.IsInContactGroup",
                          "variants": [
                            {
                              "identifier": "49c53b7f-f9aa-43b5-b622-7e6672842693",
                              "properties": {
                                "image": [ { "identifier": "{{HERO_IMAGE_IDENTIFIER}}" } ],
                                "text": "Sign up for our weekly coffee samples!",
                                "theme": null
                              },
                              "fieldIdentifiers": {
                                "image": "b27ccf6b-cd44-45cb-b087-5021dca20a55"
                              }
                            },
                            {
                              "identifier": "3cfad4b2-7df8-4460-a37a-e829b7138489",
                              "name": "Sample Requests",
                              "properties": {
                                "image": [ { "identifier": "{{HERO_IMAGE_IDENTIFIER}}" } ],
                                "text": "Try some more free samples"
                              },
                              "conditionTypeParameters": {
                                "selectedContactGroups": [
                                  { "objectGuid": null, "objectCodeName": "SampleRequestCustomer" }
                                ],
                                "variantName": null
                              }
                            }
                          ]
                        },
                        {
                          "identifier": "b1e942d9-5c3b-4aba-9b1c-db95b67db84d",
                          "type": "DancingGoat.LandingPage.ProductCardWidget",
                          "variants": [
                            {
                              "identifier": "47712e5a-3a8a-4ef3-a7fc-b749e22ccae3",
                              "properties": {
                                "selectedProducts": [
                                  { "identifier": "{{FIRST_PRODUCT_IDENTIFIER}}" },
                                  { "identifier": "{{SECOND_PRODUCT_IDENTIFIER}}" }
                                ]
                              }
                            }
                          ]
                        },
                        {
                          "identifier": "2a96a1ac-93bd-406e-9440-a31f73377192",
                          "type": "Kentico.FormWidget",
                          "variants": [
                            {
                              "identifier": "bff0d65d-9c9e-4486-991e-1e1906b7332e",
                              "properties": {
                                "selectedForm": [ { "objectGuid": null, "objectCodeName": "DancingGoatCoffeeSampleList" } ],
                                "afterSubmitRedirectToWebPage": []
                              }
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

    // A real page template configuration - flat, and its type identifier key is "identifier",
    // not the "type" key that widgets and sections use.
    private const string TEMPLATE_JSON = $$"""
        {
          "identifier": "DancingGoat.LandingPageSingleColumn",
          "properties": {
            "showLogo": true,
            "headerColorCssClass": "first-color",
            "images": [ { "identifier": "{{TEMPLATE_IMAGE_IDENTIFIER}}" } ]
          },
          "fieldIdentifiers": {
            "showLogo": "d537d190-9d65-4811-8af1-10d0f4e1a240",
            "images": "0ab07d28-484f-4d95-a11f-5b9e806151ae"
          }
        }
        """;

    private static List<WidgetReference> ReadContentReferences(string? widgetsJson, string? templateJson = null) =>
        [.. WidgetReferenceReader.ReadPageBuilderReferences(widgetsJson, templateJson).ContentReferences];

    [Test]
    public void TryGetPropertyIgnoreCase_MissingPropertyReturnsFalseWithoutThrowing()
    {
        using var document = JsonDocument.Parse("""{ "editableAreas": [] }""");

        bool found = false;
        var value = default(JsonElement);

        Assert.DoesNotThrow(() => found = WidgetReferenceReader.TryGetPropertyIgnoreCase(document.RootElement, "Sections", out value));
        Assert.Multiple(() =>
        {
            Assert.That(found, Is.False);
            Assert.That(value.ValueKind, Is.EqualTo(JsonValueKind.Undefined));
        });
    }

    [TestCase("[]")]
    [TestCase("\"text\"")]
    [TestCase("42")]
    [TestCase("true")]
    [TestCase("null")]
    public void TryGetPropertyIgnoreCase_NonObjectElementReturnsFalse(string json)
    {
        using var document = JsonDocument.Parse(json);

        bool found = false;

        Assert.DoesNotThrow(() => found = WidgetReferenceReader.TryGetPropertyIgnoreCase(document.RootElement, "Sections", out _));
        Assert.That(found, Is.False);
    }

    [Test]
    public void TryGetPropertyIgnoreCase_JsonNullPropertyValueIsFound()
    {
        using var document = JsonDocument.Parse("""{ "theme": null }""");

        bool found = WidgetReferenceReader.TryGetPropertyIgnoreCase(document.RootElement, "Theme", out var value);

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.True);
            Assert.That(value.ValueKind, Is.EqualTo(JsonValueKind.Null));
        });
    }

    [TestCase("editableAreas")]
    [TestCase("EditableAreas")]
    [TestCase("EDITABLEAREAS")]
    public void TryGetPropertyIgnoreCase_MatchesNameCaseInsensitively(string propertyName)
    {
        using var document = JsonDocument.Parse($$"""{ "{{propertyName}}": [ 1, 2 ] }""");

        bool found = WidgetReferenceReader.TryGetPropertyIgnoreCase(document.RootElement, "EditableAreas", out var value);

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.True);
            Assert.That(value.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(value.GetArrayLength(), Is.EqualTo(2));
        });
    }

    [Test]
    public void ReadPageBuilderReferences_ReadsIdentifiersFromPageBuilderConfiguration()
    {
        var references = ReadContentReferences(PAGE_BUILDER_JSON);

        Assert.Multiple(() =>
        {
            Assert.That(references.Select(reference => reference.Identifier), Is.EqualTo(new[]
            {
                Guid.Parse(SECTION_IMAGE_IDENTIFIER),
                Guid.Parse(HERO_IMAGE_IDENTIFIER),
                Guid.Parse(HERO_IMAGE_IDENTIFIER),
                Guid.Parse(FIRST_PRODUCT_IDENTIFIER),
                Guid.Parse(SECOND_PRODUCT_IDENTIFIER)
            }));
            Assert.That(references.Select(reference => reference.PropertyName), Is.EqualTo(new[]
            {
                "background",
                "image",
                "image",
                "selectedProducts",
                "selectedProducts"
            }));
            Assert.That(references.Select(reference => reference.TypeIdentifier), Is.EqualTo(new[]
            {
                "DancingGoat.SingleColumnSection",
                "DancingGoat.LandingPage.HeroImage",
                "DancingGoat.LandingPage.HeroImage",
                "DancingGoat.LandingPage.ProductCardWidget",
                "DancingGoat.LandingPage.ProductCardWidget"
            }));
        });
    }

    [Test]
    public void ReadPageBuilderReferences_SectionPropertiesAreWalked()
    {
        var reference = ReadContentReferences(PAGE_BUILDER_JSON)
            .Single(reference => reference.Identifier == Guid.Parse(SECTION_IMAGE_IDENTIFIER));

        Assert.Multiple(() =>
        {
            Assert.That(reference.SourceKind, Is.EqualTo(PageBuilderSourceKind.Section));
            Assert.That(reference.TypeIdentifier, Is.EqualTo("DancingGoat.SingleColumnSection"));
            Assert.That(reference.PropertyName, Is.EqualTo("background"));
            Assert.That(reference.Path.AreaIdentifier, Is.EqualTo("top"));
            Assert.That(reference.Path.SectionTypeIdentifier, Is.Null);
            Assert.That(reference.Path.IsPersonalizationVariant, Is.False);
        });
    }

    [Test]
    public void ReadPageBuilderReferences_WidgetReferenceCarriesTheBuilderPath()
    {
        var references = ReadContentReferences(PAGE_BUILDER_JSON)
            .Where(reference => reference.PropertyName == "image")
            .ToList();

        Assert.That(references, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            // The default variant carries neither "name" nor "conditionTypeParameters".
            Assert.That(references[0].Path.AreaIdentifier, Is.EqualTo("top"));
            Assert.That(references[0].Path.SectionTypeIdentifier, Is.EqualTo("DancingGoat.SingleColumnSection"));
            Assert.That(references[0].Path.VariantName, Is.Null);
            Assert.That(references[0].Path.IsPersonalizationVariant, Is.False);

            Assert.That(references[1].Path.VariantName, Is.EqualTo("Sample Requests"));
            Assert.That(references[1].Path.IsPersonalizationVariant, Is.True);
            Assert.That(references[1].SourceKind, Is.EqualTo(PageBuilderSourceKind.Widget));
        });
    }

    [Test]
    public void ReadPageBuilderReferences_VariantWithOnlyConditionTypeParametersIsPersonalized()
    {
        string widgetsJson = $$"""
            {
              "editableAreas": [
                {
                  "identifier": "top",
                  "sections": [
                    {
                      "type": "DancingGoat.SingleColumnSection",
                      "zones": [
                        {
                          "widgets": [
                            {
                              "type": "DancingGoat.LandingPage.HeroImage",
                              "variants": [
                                {
                                  "properties": { "image": [ { "identifier": "{{HERO_IMAGE_IDENTIFIER}}" } ] },
                                  "conditionTypeParameters": { "variantName": null }
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

        var reference = ReadContentReferences(widgetsJson).Single();

        Assert.Multiple(() =>
        {
            Assert.That(reference.Path.IsPersonalizationVariant, Is.True);
            Assert.That(reference.Path.VariantName, Is.Null);
        });
    }

    [Test]
    public void ReadPageBuilderReferences_ReadsPageTemplateConfiguration()
    {
        var references = ReadContentReferences(null, TEMPLATE_JSON);

        Assert.That(references, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(references[0].SourceKind, Is.EqualTo(PageBuilderSourceKind.Template));
            // The template's type identifier key is "identifier", not the widget "type" key.
            Assert.That(references[0].TypeIdentifier, Is.EqualTo("DancingGoat.LandingPageSingleColumn"));
            Assert.That(references[0].PropertyName, Is.EqualTo("images"));
            Assert.That(references[0].Identifier, Is.EqualTo(Guid.Parse(TEMPLATE_IMAGE_IDENTIFIER)));
            Assert.That(references[0].Path.AreaIdentifier, Is.Null);
            Assert.That(references[0].Path.SectionTypeIdentifier, Is.Null);
            Assert.That(references[0].Path.IsPersonalizationVariant, Is.False);
        });
    }

    [Test]
    public void ReadPageBuilderReferences_ReadsWidgetAndTemplateConfigurationTogether()
    {
        var references = ReadContentReferences(PAGE_BUILDER_JSON, TEMPLATE_JSON);

        Assert.That(
            references.Select(reference => reference.SourceKind),
            Is.EqualTo(new[]
            {
                PageBuilderSourceKind.Section,
                PageBuilderSourceKind.Widget,
                PageBuilderSourceKind.Widget,
                PageBuilderSourceKind.Widget,
                PageBuilderSourceKind.Widget,
                PageBuilderSourceKind.Template
            }));
    }

    [Test]
    public void ReadPageBuilderReferences_ReadsFormObjectReference()
    {
        var references = WidgetReferenceReader.ReadPageBuilderReferences(PAGE_BUILDER_JSON, TEMPLATE_JSON).ObjectReferences;

        Assert.That(references, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(references[0].ObjectCodeName, Is.EqualTo("DancingGoatCoffeeSampleList"));
            Assert.That(references[0].PropertyName, Is.EqualTo("selectedForm"));
            Assert.That(references[0].TypeIdentifier, Is.EqualTo("Kentico.FormWidget"));
            Assert.That(references[0].SourceKind, Is.EqualTo(PageBuilderSourceKind.Widget));
            Assert.That(references[0].Path.AreaIdentifier, Is.EqualTo("top"));
        });
    }

    [Test]
    public void ReadPageBuilderReferences_ConditionTypeParametersAreNotReadAsReferences()
    {
        var references = WidgetReferenceReader.ReadPageBuilderReferences(PAGE_BUILDER_JSON, null).ObjectReferences;

        // "SampleRequestCustomer" is a contact group inside conditionTypeParameters, which is out of scope.
        Assert.That(references.Select(reference => reference.ObjectCodeName), Does.Not.Contain("SampleRequestCustomer"));
    }

    [Test]
    public void ReadPageBuilderReferences_WidgetWithoutTypeYieldsReferencesWithEmptyType()
    {
        string widgetsJson = $$"""
            {
              "editableAreas": [
                {
                  "identifier": "top",
                  "sections": [
                    {
                      "identifier": "9b99f36a-f522-44d4-bfb9-fc075f1a3bc1",
                      "zones": [
                        {
                          "identifier": "fa8a8e06-42a6-43c9-960f-cfee9949025b",
                          "widgets": [
                            {
                              "identifier": "63718636-7deb-4b9a-91c2-9f5f39c8f0fd",
                              "variants": [
                                {
                                  "identifier": "49c53b7f-f9aa-43b5-b622-7e6672842693",
                                  "properties": {
                                    "image": [ { "identifier": "{{HERO_IMAGE_IDENTIFIER}}" } ]
                                  }
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

        List<WidgetReference> references = [];

        Assert.DoesNotThrow(() => references = ReadContentReferences(widgetsJson));
        Assert.That(references, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(references[0].Identifier, Is.EqualTo(Guid.Parse(HERO_IMAGE_IDENTIFIER)));
            Assert.That(references[0].PropertyName, Is.EqualTo("image"));
            Assert.That(references[0].TypeIdentifier, Is.Empty);
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("not json")]
    [TestCase("{}")]
    [TestCase("""{ "editableAreas": {} }""")]
    [TestCase("""{ "editableAreas": [ { "sections": [ { "zones": [] } ] } ] }""")]
    public void ReadPageBuilderReferences_InvalidOrUnrelatedConfigurationReturnsEmpty(string? widgetsJson)
    {
        List<WidgetReference> references = [];

        Assert.DoesNotThrow(() => references = ReadContentReferences(widgetsJson));
        Assert.That(references, Is.Empty);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("not json")]
    [TestCase("[]")]
    [TestCase("{}")]
    [TestCase("""{ "identifier": "DancingGoat.LandingPageSingleColumn" }""")]
    [TestCase("""{ "identifier": "DancingGoat.LandingPageSingleColumn", "properties": [] }""")]
    public void ReadPageBuilderReferences_InvalidOrUnrelatedTemplateConfigurationReturnsEmpty(string? templateJson)
    {
        List<WidgetReference> references = [];

        Assert.DoesNotThrow(() => references = ReadContentReferences(null, templateJson));
        Assert.That(references, Is.Empty);
    }

    [Test]
    public void ExtractWidgetPropertyIdentifiers_ReadsJsonEncodedStringValues()
    {
        var contentItemIdentifier = Guid.Parse(FIRST_PRODUCT_IDENTIFIER);
        var webPageIdentifier = Guid.Parse(SECOND_PRODUCT_IDENTIFIER);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(
            $$"""[{ "Identifier": "{{contentItemIdentifier}}" }, { "WebPageGuid": "{{webPageIdentifier}}" }]"""));

        var identifiers = WidgetReferenceReader.ExtractWidgetPropertyIdentifiers(document.RootElement);

        Assert.That(identifiers, Is.EqualTo(new[] { contentItemIdentifier, webPageIdentifier }));
    }

    [Test]
    public void ExtractWidgetPropertyIdentifiers_ReadsPlainGuidArray()
    {
        var identifier = Guid.Parse(HERO_IMAGE_IDENTIFIER);
        using var document = JsonDocument.Parse($$"""[ "{{identifier}}", "not a guid" ]""");

        var identifiers = WidgetReferenceReader.ExtractWidgetPropertyIdentifiers(document.RootElement);

        Assert.That(identifiers, Is.EqualTo(new[] { identifier }));
    }

    [TestCase("null")]
    [TestCase("42")]
    [TestCase("true")]
    [TestCase("{}")]
    [TestCase("\"plain text\"")]
    [TestCase("""[ { "objectCodeName": "DancingGoatCoffeeSampleList" } ]""")]
    public void ExtractWidgetPropertyIdentifiers_UnrelatedValueReturnsEmpty(string json)
    {
        using var document = JsonDocument.Parse(json);

        var identifiers = WidgetReferenceReader.ExtractWidgetPropertyIdentifiers(document.RootElement);

        Assert.That(identifiers, Is.Empty);
    }

    [Test]
    public void ExtractWidgetPropertyObjectCodeNames_ReadsCodeNameWhenObjectGuidIsNull()
    {
        using var document = JsonDocument.Parse(
            """[ { "objectGuid": null, "objectCodeName": "DancingGoatCoffeeSampleList" } ]""");

        var codeNames = WidgetReferenceReader.ExtractWidgetPropertyObjectCodeNames(document.RootElement);

        Assert.That(codeNames, Is.EqualTo(new[] { "DancingGoatCoffeeSampleList" }));
    }

    [TestCase("null")]
    [TestCase("42")]
    [TestCase("{}")]
    [TestCase("[]")]
    [TestCase("\"DancingGoatCoffeeSampleList\"")]
    [TestCase("""[ { "objectCodeName": null } ]""")]
    [TestCase("""[ { "objectCodeName": "   " } ]""")]
    [TestCase($$"""[ { "identifier": "{{HERO_IMAGE_IDENTIFIER}}" } ]""")]
    public void ExtractWidgetPropertyObjectCodeNames_UnrelatedValueReturnsEmpty(string json)
    {
        using var document = JsonDocument.Parse(json);

        var codeNames = WidgetReferenceReader.ExtractWidgetPropertyObjectCodeNames(document.RootElement);

        Assert.That(codeNames, Is.Empty);
    }
}
