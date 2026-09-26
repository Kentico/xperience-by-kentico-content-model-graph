using RelationshipFieldSource = Kentico.Xperience.ContentModelGraph.ContentItemRelationshipGraphBuilder.RelationshipFieldSource;
using ResolvedReferenceSource = Kentico.Xperience.ContentModelGraph.ContentItemRelationshipGraphBuilder.ResolvedReferenceSource;

namespace Kentico.Xperience.ContentModelGraph.Tests;

/// <summary>
/// Covers how a reference row is labelled from its <c>ContentItemReferenceGroupGUID</c>: first against the
/// referencing item's content type fields, then against its builder <c>fieldIdentifiers</c>, and only then by
/// searching field values for the target.
/// </summary>
public class ReferenceGroupResolutionTests
{
    private const int SOURCE_ITEM_ID = 141;
    private const int TARGET_ITEM_ID = 7;

    private static readonly Guid relatedArticlesField = new("11111111-aaaa-4aaa-8aaa-111111111111");
    private static readonly Guid featuredArticlesField = new("22222222-bbbb-4bbb-8bbb-222222222222");
    private static readonly Guid unknownGroup = new("33333333-cccc-4ccc-8ccc-333333333333");
    private static readonly Guid otherUnknownGroup = new("44444444-dddd-4ddd-8ddd-444444444444");

    private static readonly RelationshipFieldSource relatedArticles = new("Related articles", "ArticleRelatedArticles");
    private static readonly RelationshipFieldSource featuredArticles = new("Featured articles", "ArticleFeaturedArticles");

    private static readonly Dictionary<Guid, RelationshipFieldSource> contentTypeFields = new()
    {
        [relatedArticlesField] = relatedArticles,
        [featuredArticlesField] = featuredArticles
    };

    private static readonly Dictionary<Guid, PageBuilderFieldIdentifier> noBuilderFields = [];

    private static Func<IReadOnlyList<RelationshipFieldSource>> NoValueSearch =>
        () => throw new AssertionException("The value search ran although every row could be placed by its group.");

    private static IReadOnlyList<ResolvedReferenceSource> Resolve(
        IEnumerable<Guid> groups,
        IReadOnlyDictionary<Guid, PageBuilderFieldIdentifier>? builderFields = null,
        Func<IReadOnlyList<RelationshipFieldSource>>? matchByValue = null) =>
        ContentItemRelationshipGraphBuilder.ResolveReferenceSources(
            groups,
            contentTypeFields,
            builderFields ?? noBuilderFields,
            matchByValue ?? NoValueSearch);

    private static List<ContentItemRelationship> ToRelationships(IEnumerable<ResolvedReferenceSource> resolved)
    {
        var relationships = new List<ContentItemRelationship>();
        foreach (var row in resolved)
        {
            ContentItemRelationshipGraphBuilder.AddRelationships(
                relationships,
                new ContentItemRelationshipItem
                {
                    ItemId = TARGET_ITEM_ID,
                    DisplayName = "Target",
                    CodeName = "Target",
                    ContentTypeDisplayName = "Article",
                    ContentTypeCodeName = "DancingGoat.Article",
                    Kind = GraphNodeKind.WEBSITE
                },
                SOURCE_ITEM_ID,
                TARGET_ITEM_ID,
                row.ReferenceGroup,
                "outgoing",
                row.Sources);
        }

        return relationships;
    }

    [Test]
    public void ContentTypeFieldGuid_LabelsTheEdgeWithTheField()
    {
        var relationship = ToRelationships(Resolve([relatedArticlesField])).Single();

        Assert.Multiple(() =>
        {
            Assert.That(relationship.FieldLabel, Is.EqualTo("Related articles"));
            Assert.That(relationship.FieldCodeName, Is.EqualTo("ArticleRelatedArticles"));
            Assert.That(relationship.Id, Is.EqualTo(
                "item:141=>item:7:11111111-aaaa-4aaa-8aaa-111111111111:ArticleRelatedArticles"));
        });
    }

    // The bug the value search caused: one target selected in two fields is two rows, and matching values put
    // both rows in both fields - four edges, two of them labelled with the other row's field.
    [Test]
    public void TargetSelectedInTwoFields_GivesOneCorrectlyLabelledEdgePerField()
    {
        var relationships = ToRelationships(Resolve(
            [relatedArticlesField, featuredArticlesField],
            matchByValue: () => [relatedArticles, featuredArticles]));

        Assert.That(relationships.Select(relationship => relationship.Id), Is.EquivalentTo(new[]
        {
            "item:141=>item:7:11111111-aaaa-4aaa-8aaa-111111111111:ArticleRelatedArticles",
            "item:141=>item:7:22222222-bbbb-4bbb-8bbb-222222222222:ArticleFeaturedArticles"
        }));
    }

    [Test]
    public void BuilderFieldIdentifier_LabelsTheEdgeWithTheWidgetAndProperty()
    {
        var builderFields = new Dictionary<Guid, PageBuilderFieldIdentifier>
        {
            [unknownGroup] = new(HeroImagePath(null, false), "image", unknownGroup)
        };

        var relationship = ToRelationships(Resolve([unknownGroup], builderFields)).Single();

        Assert.Multiple(() =>
        {
            Assert.That(relationship.FieldLabel, Is.EqualTo("Widget: DancingGoat.LandingPage.HeroImage"));
            Assert.That(relationship.FieldCodeName, Is.EqualTo("widget:DancingGoat.LandingPage.HeroImage:image"));
            Assert.That(relationship.FieldPath, Is.EqualTo(
                "top › DancingGoat.SingleColumnSection › DancingGoat.LandingPage.HeroImage › image"));
        });
    }

    // Each personalization variant has its own group GUID for a property, so one property used on two variants is
    // two rows. They stay one edge, as they did when the edge was found by value.
    [Test]
    public void OnePropertyOnTwoVariants_StaysOneEdgeCarryingTheLowestGroup()
    {
        var builderFields = new Dictionary<Guid, PageBuilderFieldIdentifier>
        {
            [otherUnknownGroup] = new(HeroImagePath(null, false), "image", otherUnknownGroup),
            [unknownGroup] = new(HeroImagePath("Sample Requests", true), "image", unknownGroup)
        };

        var relationship = ToRelationships(Resolve([otherUnknownGroup, unknownGroup], builderFields)).Single();

        Assert.Multiple(() =>
        {
            Assert.That(relationship.Id, Is.EqualTo(
                "item:141=>item:7:33333333-cccc-4ccc-8ccc-333333333333:widget:DancingGoat.LandingPage.HeroImage:image"));
            // Also on the default variant, so not marked as hidden behind a variant.
            Assert.That(relationship.FieldLabel, Is.EqualTo("Widget: DancingGoat.LandingPage.HeroImage"));
            Assert.That(relationship.FieldPath!.Split('\n'), Has.Length.EqualTo(2));
        });
    }

    [Test]
    public void PropertyOnlyOnAPersonalizationVariant_IsMarkedWithTheVariant()
    {
        var builderFields = new Dictionary<Guid, PageBuilderFieldIdentifier>
        {
            [unknownGroup] = new(HeroImagePath("Sample Requests", true), "image", unknownGroup)
        };

        var relationship = ToRelationships(Resolve([unknownGroup], builderFields)).Single();

        Assert.That(relationship.FieldLabel, Is.EqualTo("Widget: DancingGoat.LandingPage.HeroImage · variant \"Sample Requests\""));
    }

    [Test]
    public void SectionAndTemplateFieldIdentifiers_AreLabelledByTheirKind()
    {
        var builderFields = new Dictionary<Guid, PageBuilderFieldIdentifier>
        {
            [unknownGroup] = new(new PageBuilderReferencePath(PageBuilderSourceKind.Section, "DancingGoat.SingleColumnSection"), "background", unknownGroup),
            [otherUnknownGroup] = new(new PageBuilderReferencePath(PageBuilderSourceKind.Template, "DancingGoat.LandingPageSingleColumn"), "images", otherUnknownGroup)
        };

        var labels = ToRelationships(Resolve([unknownGroup, otherUnknownGroup], builderFields)).Select(relationship => relationship.FieldLabel);

        Assert.That(labels, Is.EquivalentTo(new[]
        {
            "Section: DancingGoat.SingleColumnSection",
            "Template: DancingGoat.LandingPageSingleColumn"
        }));
    }

    // Builder configuration saved before fieldIdentifiers existed: the value search still labels the row, with
    // the same id the edge had before groups were resolved.
    [Test]
    public void UnknownGroup_FallsBackToTheValueSearch()
    {
        var widget = new RelationshipFieldSource("Widget: DancingGoat.LandingPage.HeroImage", "widget:DancingGoat.LandingPage.HeroImage:image");

        var relationship = ToRelationships(Resolve([unknownGroup], matchByValue: () => [widget])).Single();

        Assert.Multiple(() =>
        {
            Assert.That(relationship.FieldLabel, Is.EqualTo(widget.Label));
            Assert.That(relationship.Id, Is.EqualTo(
                "item:141=>item:7:33333333-cccc-4ccc-8ccc-333333333333:widget:DancingGoat.LandingPage.HeroImage:image"));
        });
    }

    [Test]
    public void UnknownGroupWithNoValueMatch_IsOneUnlabelledEdge()
    {
        var relationship = ToRelationships(Resolve([unknownGroup], matchByValue: () => [])).Single();

        Assert.Multiple(() =>
        {
            Assert.That(relationship.FieldLabel, Is.Empty);
            Assert.That(relationship.FieldCodeName, Is.Empty);
            Assert.That(relationship.Id, Is.EqualTo("item:141=>item:7:33333333-cccc-4ccc-8ccc-333333333333"));
        });
    }

    // Nothing is known about which unplaced row a value match belongs to, so the matches go to one row rather
    // than to every row.
    [Test]
    public void SeveralUnknownGroups_ShareTheValueMatchesInsteadOfMultiplyingThem()
    {
        var relationships = ToRelationships(Resolve(
            [otherUnknownGroup, unknownGroup],
            matchByValue: () => [relatedArticles]));

        Assert.Multiple(() =>
        {
            Assert.That(relationships, Has.Count.EqualTo(1));
            Assert.That(relationships[0].Id, Does.Contain(unknownGroup.ToString("D")));
        });
    }

    [Test]
    public void SeveralUnknownGroupsWithNoValueMatch_AreOneUnlabelledEdgeEach()
    {
        var relationships = ToRelationships(Resolve([otherUnknownGroup, unknownGroup], matchByValue: () => []));

        Assert.Multiple(() =>
        {
            Assert.That(relationships, Has.Count.EqualTo(2));
            Assert.That(relationships.Select(relationship => relationship.Id), Is.Unique);
            Assert.That(relationships.Select(relationship => relationship.FieldLabel), Is.All.Empty);
        });
    }

    // The value search cannot put an unplaced row in a field another row of the pair was already placed in.
    [Test]
    public void ValueSearch_DoesNotReuseAFieldAlreadyPlacedByGroup()
    {
        var relationships = ToRelationships(Resolve(
            [relatedArticlesField, unknownGroup],
            matchByValue: () => [relatedArticles, featuredArticles]));

        Assert.That(relationships.Select(relationship => relationship.Id), Is.EquivalentTo(new[]
        {
            "item:141=>item:7:11111111-aaaa-4aaa-8aaa-111111111111:ArticleRelatedArticles",
            "item:141=>item:7:33333333-cccc-4ccc-8ccc-333333333333:ArticleFeaturedArticles"
        }));
    }

    [Test]
    public void ContentTypeField_IsPreferredOverABuilderEntryWithTheSameGuid()
    {
        var builderFields = new Dictionary<Guid, PageBuilderFieldIdentifier>
        {
            [relatedArticlesField] = new(HeroImagePath(null, false), "image", relatedArticlesField)
        };

        var relationship = ToRelationships(Resolve([relatedArticlesField], builderFields)).Single();

        Assert.That(relationship.FieldCodeName, Is.EqualTo("ArticleRelatedArticles"));
    }

    [Test]
    public void IndexFieldIdentifiers_FirstConfigurationToNameAGroupWins()
    {
        var first = new PageBuilderFieldIdentifier(HeroImagePath(null, false), "image", unknownGroup);
        var second = new PageBuilderFieldIdentifier(HeroImagePath(null, false), "text", unknownGroup);

        var index = ContentItemRelationshipGraphBuilder.IndexFieldIdentifiers(
        [
            new PageBuilderReferences([], [], [first]),
            new PageBuilderReferences([], [], [second])
        ]);

        Assert.That(index[unknownGroup], Is.SameAs(first));
    }

    // The Dancing Goat coffee-samples page: three Kentico.Widget.RichText widgets link to pages from their
    // content. Nothing in an HTML value names a content item GUID, so the value search finds nothing, and the
    // edges were drawn unlabelled. Each stored reference row's group is the variant's fieldIdentifiers.content.
    [TestCase("d06a3e43-5309-40cb-b51b-2ad14bd515de", "bottom › DancingGoat.SingleColumnSection › Kentico.Widget.RichText › content", TestName = "RichText link to Articles is labelled")]
    [TestCase("aba0ede5-9907-4528-83f5-c1a51eea9367", "bottom › DancingGoat.SingleColumnSection › Kentico.Widget.RichText › content", TestName = "RichText link to Grinders is labelled")]
    [TestCase("4b1da05e-2a56-4870-9620-99579fe7b4c6", "bottom › DancingGoat.TwoColumnSection › Kentico.Widget.RichText › content", TestName = "RichText link to Coffee Beverages Explained is labelled")]
    public void CoffeeSamplesRichTextLinks_AreLabelledFromTheirGroup(string group, string expectedPath)
    {
        string widgetsJson = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "coffee-samples-widgets.json"));
        var references = WidgetReferenceReader.ReadPageBuilderReferences(widgetsJson, null);

        var relationship = ToRelationships(ContentItemRelationshipGraphBuilder.ResolveReferenceSources(
            [Guid.Parse(group)],
            new Dictionary<Guid, RelationshipFieldSource>(),
            ContentItemRelationshipGraphBuilder.IndexFieldIdentifiers([references]),
            NoValueSearch)).Single();

        Assert.Multiple(() =>
        {
            Assert.That(relationship.FieldLabel, Is.EqualTo("Widget: Kentico.Widget.RichText"));
            Assert.That(relationship.FieldCodeName, Is.EqualTo("widget:Kentico.Widget.RichText:content"));
            Assert.That(relationship.FieldPath, Is.EqualTo(expectedPath));
            Assert.That(relationship.Id, Is.EqualTo($"item:141=>item:7:{group}:widget:Kentico.Widget.RichText:content"));
        });
    }

    private static PageBuilderReferencePath HeroImagePath(string? variantName, bool isPersonalized) =>
        new(PageBuilderSourceKind.Widget, "DancingGoat.LandingPage.HeroImage")
        {
            AreaIdentifier = "top",
            SectionTypeIdentifier = "DancingGoat.SingleColumnSection",
            VariantName = variantName,
            IsPersonalizationVariant = isPersonalized
        };
}
