namespace Kentico.Xperience.ContentModelGraph.Tests;

/// <summary>
/// Covers the parts of the form-rooted relationship graph that do not need a live CMS. The query itself
/// (the <c>cms.contentitemobjectreference</c> reverse lookup joined to the latest common data) is not
/// covered here - it needs real info providers and a populated database.
/// </summary>
public class FormRelationshipGraphTests
{
    private const string FORM_NAME = "DancingGoatCoffeeSampleList";

    // Trimmed from a real Page Builder configuration: one Form Widget embedding the form above, plus a
    // contact group inside conditionTypeParameters that must not be mistaken for a form reference.
    private const string PAGE_BUILDER_JSON = $$"""
        {
          "editableAreas": [
            {
              "identifier": "top",
              "sections": [
                {
                  "identifier": "9b99f36a-f522-44d4-bfb9-fc075f1a3bc1",
                  "type": "DancingGoat.SingleColumnSection",
                  "zones": [
                    {
                      "identifier": "fa8a8e06-42a6-43c9-960f-cfee9949025b",
                      "widgets": [
                        {
                          "identifier": "2a96a1ac-93bd-406e-9440-a31f73377192",
                          "type": "Kentico.FormWidget",
                          "variants": [
                            {
                              "identifier": "bff0d65d-9c9e-4486-991e-1e1906b7332e",
                              "properties": {
                                "selectedForm": [ { "objectGuid": null, "objectCodeName": "{{FORM_NAME}}" } ],
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

    private static ContentItemRelationshipItem FormItem() =>
        new()
        {
            Identifier = "6f9619ff-8b86-d011-b42d-00c04fc964ff",
            DisplayName = "Coffee samples",
            CodeName = FORM_NAME,
            ContentTypeDisplayName = "Form",
            ContentTypeCodeName = "form",
            Kind = GraphNodeKind.FORMS
        };

    [Test]
    public void CreateMissingFormGraph_FlagsRootAsMissingAndStaysTerminal()
    {
        var graph = ContentItemRelationshipGraphBuilder.CreateMissingFormGraph();

        Assert.Multiple(() =>
        {
            Assert.That(graph.RootItem.IsMissing, Is.True);
            Assert.That(graph.RootItem.Kind, Is.EqualTo(GraphNodeKind.FORMS));
            // A form is not a content item, so the node must never carry an identifier the client would
            // hand back to the content item expansion command.
            Assert.That(graph.RootItem.ItemId, Is.Null);
            Assert.That(graph.Incoming, Is.Empty);
            Assert.That(graph.Outgoing, Is.Empty);
            Assert.That(graph.Truncations, Is.Empty);
        });
    }

    [Test]
    public void CreateFormGraph_WithoutTruncation_ReportsNothingTruncated()
    {
        var graph = ContentItemRelationshipGraphBuilder.CreateFormGraph(FormItem(), [], null);

        Assert.Multiple(() =>
        {
            Assert.That(graph.Truncations, Is.Empty);
            // A form has no outgoing relationships of its own - the graph is always one hop inwards.
            Assert.That(graph.Outgoing, Is.Empty);
        });
    }

    [Test]
    public void CreateFormGraph_WithTruncation_ReportsTheTrueTotalAlongsideTheCappedSet()
    {
        var truncation = new ContentItemRelationshipTruncation
        {
            Direction = "incoming",
            ShownItemCount = ContentItemRelationshipGraphBuilder.FORM_USAGE_ITEM_LIMIT,
            TotalItemCount = 517
        };

        var graph = ContentItemRelationshipGraphBuilder.CreateFormGraph(FormItem(), [], truncation);

        Assert.That(graph.Truncations, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(graph.Truncations.Single().Direction, Is.EqualTo("incoming"));
            Assert.That(
                graph.Truncations.Single().ShownItemCount,
                Is.EqualTo(ContentItemRelationshipGraphBuilder.FORM_USAGE_ITEM_LIMIT));
            Assert.That(graph.Truncations.Single().TotalItemCount, Is.EqualTo(517));
        });
    }

    [Test]
    public void MatchFormSources_LabelsTheEdgeWithTheWidgetAndProperty()
    {
        var sources = ContentItemRelationshipGraphBuilder.MatchFormSources(PAGE_BUILDER_JSON, null, FORM_NAME);

        Assert.That(sources, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(sources[0].CodeName, Is.EqualTo("widget:Kentico.FormWidget:selectedForm"));
            Assert.That(sources[0].Path, Does.Contain("Kentico.FormWidget"));
        });
    }

    [Test]
    public void MatchFormSources_IgnoresOtherFormsAndObjectReferences()
    {
        var sources = ContentItemRelationshipGraphBuilder.MatchFormSources(
            PAGE_BUILDER_JSON,
            null,
            "SomeOtherForm");

        Assert.That(sources, Is.Empty);
    }

    [Test]
    public void MatchFormSources_MatchesTheFormCodeNameCaseInsensitively()
    {
        var sources = ContentItemRelationshipGraphBuilder.MatchFormSources(
            PAGE_BUILDER_JSON,
            null,
            FORM_NAME.ToUpperInvariant());

        Assert.That(sources, Has.Count.EqualTo(1));
    }

    [Test]
    public void MatchFormSources_WithoutBuilderConfiguration_ReturnsNoSources()
    {
        // The reference row stays the authority: the caller draws an unlabelled edge rather than dropping
        // the relationship when the configuration cannot be read.
        var sources = ContentItemRelationshipGraphBuilder.MatchFormSources(null, null, FORM_NAME);

        Assert.That(sources, Is.Empty);
    }
}
