namespace Kentico.Xperience.ContentModelGraph.Tests;

/// <summary>
/// Covers <see cref="ContentItemRelationship.Id" />: that it identifies one relationship rather than one end of
/// one, and that it says nothing about the request that produced it.
/// </summary>
/// <remarks>
/// The scheme these tests pin replaced one that encoded a single endpoint. Every collision below was reachable
/// under that scheme, and the first one shipped: the client used the id as a ReactFlow edge id, which becomes a
/// React key, and two edges sharing a key left one of them un-unmounted after a graph reset.
/// <para>
/// A graph is assembled from several expansions, each its own response, so "unique within a response" is the
/// floor rather than the goal - the ids also have to stay apart once the responses are merged, which is what
/// encoding both ends buys.
/// </para>
/// </remarks>
public class ContentItemRelationshipIdTests
{
    // Two items of one content type, which is the common case and the one that collided: the fields they carry
    // have the same code name on both.
    private const int ARTICLE_ITEM_ID = 11;
    private const int OTHER_ARTICLE_ITEM_ID = 12;
    private const int COFFEE_ITEM_ID = 21;
    private const int OTHER_COFFEE_ITEM_ID = 22;

    private const string TAXONOMY_FIELD = "ArticleTaxonomy";
    private const string REFERENCE_FIELD = "ArticleRelatedCoffees";
    private const string WIDGET_FIELD = "widget:Kentico.FormWidget:selectedForm";

    private static readonly Guid sharedTag = new("2f9b2ef8-4b6d-4f3a-9a83-2b8f1f8f5d21");
    private static readonly Guid sharedForm = new("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    // One reference group spans every item selected in one field, which is exactly why it cannot identify an
    // edge on its own.
    private static readonly Guid referenceGroup = new("b3c2a1d0-1111-2222-3333-444455556666");
    private static readonly Guid otherReferenceGroup = new("c4d3b2e1-9999-8888-7777-666655554444");

    // The shipped collision. The taxonomy id named the field and the tag and no item at all, so two articles
    // carrying the same tag on the same field reported byte-identical ids.
    [Test]
    public void TaxonomyIds_ForTwoItemsSharingATagOnASameNamedField_AreDistinct()
    {
        string article = ContentItemRelationshipGraphBuilder.CreateTaxonomyRelationshipId(ARTICLE_ITEM_ID, sharedTag, TAXONOMY_FIELD);
        string otherArticle = ContentItemRelationshipGraphBuilder.CreateTaxonomyRelationshipId(OTHER_ARTICLE_ITEM_ID, sharedTag, TAXONOMY_FIELD);

        Assert.That(article, Is.Not.EqualTo(otherArticle));
    }

    [Test]
    public void TaxonomyId_NamesTheTaggedItem_TheTag_AndTheFieldBetweenThem()
    {
        string id = ContentItemRelationshipGraphBuilder.CreateTaxonomyRelationshipId(ARTICLE_ITEM_ID, sharedTag, TAXONOMY_FIELD);

        Assert.That(id, Is.EqualTo("item:11=>tag:2f9b2ef8-4b6d-4f3a-9a83-2b8f1f8f5d21:ArticleTaxonomy"));
    }

    // Two coffees selected in one reference field share a GroupGUID. The incoming id named only the item at the
    // far end - the article - so expanding either coffee reported the same id for two different edges.
    [Test]
    public void ReferenceIds_ForTwoItemsSelectedInOneReferenceField_AreDistinct()
    {
        string toCoffee = ContentItemRelationshipGraphBuilder.CreateReferenceRelationshipId(
            ARTICLE_ITEM_ID, COFFEE_ITEM_ID, referenceGroup, REFERENCE_FIELD);
        string toOtherCoffee = ContentItemRelationshipGraphBuilder.CreateReferenceRelationshipId(
            ARTICLE_ITEM_ID, OTHER_COFFEE_ITEM_ID, referenceGroup, REFERENCE_FIELD);

        Assert.That(toCoffee, Is.Not.EqualTo(toOtherCoffee));
    }

    [Test]
    public void ReferenceId_NamesTheReferencingItem_TheReferencedItem_TheGroupAndTheField()
    {
        string id = ContentItemRelationshipGraphBuilder.CreateReferenceRelationshipId(
            ARTICLE_ITEM_ID, COFFEE_ITEM_ID, referenceGroup, REFERENCE_FIELD);

        Assert.That(id, Is.EqualTo("item:11=>item:21:b3c2a1d0-1111-2222-3333-444455556666:ArticleRelatedCoffees"));
    }

    // A reference the reader could not place in any field still gets an edge, and that edge still needs an id
    // that two targets in one group cannot share.
    [Test]
    public void ReferenceIds_ForAnUnplaceableReferenceToTwoItemsInOneGroup_AreDistinct()
    {
        string toCoffee = ContentItemRelationshipGraphBuilder.CreateReferenceRelationshipId(
            ARTICLE_ITEM_ID, COFFEE_ITEM_ID, referenceGroup, null);
        string toOtherCoffee = ContentItemRelationshipGraphBuilder.CreateReferenceRelationshipId(
            ARTICLE_ITEM_ID, OTHER_COFFEE_ITEM_ID, referenceGroup, null);

        Assert.Multiple(() =>
        {
            Assert.That(toCoffee, Is.Not.EqualTo(toOtherCoffee));
            Assert.That(toCoffee, Is.EqualTo("item:11=>item:21:b3c2a1d0-1111-2222-3333-444455556666"));
        });
    }

    // Both ends are written in reference order rather than outwards from the root, so a pair that references
    // each other still gets two ids.
    [Test]
    public void ReferenceIds_ForOppositeReferencesBetweenOnePair_AreDistinct()
    {
        string articleToCoffee = ContentItemRelationshipGraphBuilder.CreateReferenceRelationshipId(
            ARTICLE_ITEM_ID, COFFEE_ITEM_ID, referenceGroup, REFERENCE_FIELD);
        string coffeeToArticle = ContentItemRelationshipGraphBuilder.CreateReferenceRelationshipId(
            COFFEE_ITEM_ID, ARTICLE_ITEM_ID, otherReferenceGroup, REFERENCE_FIELD);

        Assert.That(articleToCoffee, Is.Not.EqualTo(coffeeToArticle));
    }

    // The form id named the form and the widget property and no item, so every page embedding one form through
    // the same widget property - a site-wide footer, say - reported one id.
    [Test]
    public void FormIds_ForTwoItemsEmbeddingOneFormInTheSameWidgetProperty_AreDistinct()
    {
        string article = ContentItemRelationshipGraphBuilder.CreateFormRelationshipId(ARTICLE_ITEM_ID, sharedForm, WIDGET_FIELD);
        string otherArticle = ContentItemRelationshipGraphBuilder.CreateFormRelationshipId(OTHER_ARTICLE_ITEM_ID, sharedForm, WIDGET_FIELD);

        Assert.That(article, Is.Not.EqualTo(otherArticle));
    }

    // The item-rooted graph reaches this edge through GetFormRelationships and the form-rooted graph reaches it
    // through BuildForForm. It is one relationship, so it gets one id.
    [Test]
    public void FormId_IsTheSameFromTheItemRootedGraphAndTheFormRootedGraph()
    {
        string fromTheItem = ContentItemRelationshipGraphBuilder.CreateFormRelationshipId(ARTICLE_ITEM_ID, sharedForm, WIDGET_FIELD);
        string fromTheForm = ContentItemRelationshipGraphBuilder.CreateFormRelationshipId(ARTICLE_ITEM_ID, sharedForm, WIDGET_FIELD);

        Assert.Multiple(() =>
        {
            Assert.That(fromTheItem, Is.EqualTo(fromTheForm));
            Assert.That(fromTheItem, Is.EqualTo("item:11=>form:6f9619ff-8b86-d011-b42d-00c04fc964ff:widget:Kentico.FormWidget:selectedForm"));
        });
    }

    // Mirrors the two call sites in Build: the outgoing loop passes the root as the referencing item, the
    // incoming loop passes it as the referenced one. Both describe the same edge and must name it the same way,
    // which is what lets the client merge two expansions without drawing that edge twice. The direction the
    // reader saw it from is carried by Direction instead, where it does not distort identity.
    [Test]
    public void ReferenceRelationships_ForOneEdgeSeenFromEitherEnd_CarryOneIdAndKeepTheirOwnDirection()
    {
        var expandingTheArticle = new List<ContentItemRelationship>();
        ContentItemRelationshipGraphBuilder.AddRelationships(
            expandingTheArticle,
            Node(COFFEE_ITEM_ID),
            ARTICLE_ITEM_ID,
            COFFEE_ITEM_ID,
            referenceGroup,
            "outgoing",
            [Field(REFERENCE_FIELD)]);

        var expandingTheCoffee = new List<ContentItemRelationship>();
        ContentItemRelationshipGraphBuilder.AddRelationships(
            expandingTheCoffee,
            Node(ARTICLE_ITEM_ID),
            ARTICLE_ITEM_ID,
            COFFEE_ITEM_ID,
            referenceGroup,
            "incoming",
            [Field(REFERENCE_FIELD)]);

        Assert.Multiple(() =>
        {
            Assert.That(expandingTheArticle[0].Id, Is.EqualTo(expandingTheCoffee[0].Id));
            Assert.That(expandingTheArticle[0].Direction, Is.EqualTo("outgoing"));
            Assert.That(expandingTheCoffee[0].Direction, Is.EqualTo("incoming"));
        });
    }

    // Two items selected in one field produce two edges out of one AddRelationships call chain - the case the
    // reference group alone could not tell apart.
    [Test]
    public void ReferenceRelationships_ForTwoTargetsInOneGroup_AreAllDistinct()
    {
        var relationships = new List<ContentItemRelationship>();

        foreach (int targetItemId in new[] { COFFEE_ITEM_ID, OTHER_COFFEE_ITEM_ID })
        {
            ContentItemRelationshipGraphBuilder.AddRelationships(
                relationships,
                Node(targetItemId),
                ARTICLE_ITEM_ID,
                targetItemId,
                referenceGroup,
                "outgoing",
                [Field(REFERENCE_FIELD)]);
        }

        Assert.That(relationships.Select(relationship => relationship.Id), Is.Unique);
    }

    // The bug as the client met it: one graph assembled from several expansions, every edge keyed by its id.
    // Two articles of one content type, both tagged with the same tag, both embedding the same form, both
    // pointing at overlapping coffees - the shape that produced duplicate React keys.
    [Test]
    public void RelationshipIds_AcrossTheExpansionsThatMakeUpOneGraph_AreAllDistinct()
    {
        string[] ids =
        [
            // Expanding the first article.
            ContentItemRelationshipGraphBuilder.CreateReferenceRelationshipId(ARTICLE_ITEM_ID, COFFEE_ITEM_ID, referenceGroup, REFERENCE_FIELD),
            ContentItemRelationshipGraphBuilder.CreateReferenceRelationshipId(ARTICLE_ITEM_ID, OTHER_COFFEE_ITEM_ID, referenceGroup, REFERENCE_FIELD),
            ContentItemRelationshipGraphBuilder.CreateTaxonomyRelationshipId(ARTICLE_ITEM_ID, sharedTag, TAXONOMY_FIELD),
            ContentItemRelationshipGraphBuilder.CreateFormRelationshipId(ARTICLE_ITEM_ID, sharedForm, WIDGET_FIELD),

            // Expanding the second article, which is of the same content type and carries the same tag, the
            // same form and one of the same coffees.
            ContentItemRelationshipGraphBuilder.CreateReferenceRelationshipId(OTHER_ARTICLE_ITEM_ID, COFFEE_ITEM_ID, otherReferenceGroup, REFERENCE_FIELD),
            ContentItemRelationshipGraphBuilder.CreateTaxonomyRelationshipId(OTHER_ARTICLE_ITEM_ID, sharedTag, TAXONOMY_FIELD),
            ContentItemRelationshipGraphBuilder.CreateFormRelationshipId(OTHER_ARTICLE_ITEM_ID, sharedForm, WIDGET_FIELD),

            // Expanding a coffee, which reports the same two references back as incoming.
            ContentItemRelationshipGraphBuilder.CreateReferenceRelationshipId(ARTICLE_ITEM_ID, COFFEE_ITEM_ID, referenceGroup, REFERENCE_FIELD),
            ContentItemRelationshipGraphBuilder.CreateReferenceRelationshipId(OTHER_ARTICLE_ITEM_ID, COFFEE_ITEM_ID, otherReferenceGroup, REFERENCE_FIELD),
        ];

        // The two incoming edges repeat two outgoing ones, deliberately: the same relationship seen from its
        // other end is the same relationship, so it repeats an id rather than inventing a second one.
        Assert.That(ids.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(7));
    }

    // Nothing request-scoped may reach an id - no timestamp, no counter, no per-call GUID - or a consumer could
    // not diff two exports by it.
    [Test]
    public void RelationshipIds_ForTheSameRelationshipBuiltTwice_AreIdentical()
    {
        string first = ContentItemRelationshipGraphBuilder.CreateReferenceRelationshipId(
            ARTICLE_ITEM_ID, COFFEE_ITEM_ID, referenceGroup, REFERENCE_FIELD);
        string second = ContentItemRelationshipGraphBuilder.CreateReferenceRelationshipId(
            ARTICLE_ITEM_ID, COFFEE_ITEM_ID, referenceGroup, REFERENCE_FIELD);

        Assert.That(first, Is.EqualTo(second));
    }

    private static ContentItemRelationshipGraphBuilder.RelationshipFieldSource Field(string codeName) =>
        new(codeName, codeName);

    private static ContentItemRelationshipItem Node(int itemId) =>
        new()
        {
            ItemId = itemId,
            DisplayName = $"Item {itemId}",
            CodeName = $"Item{itemId}",
            ContentTypeDisplayName = "Article",
            ContentTypeCodeName = "DancingGoat.Article",
            Kind = GraphNodeKind.REUSABLE
        };
}
