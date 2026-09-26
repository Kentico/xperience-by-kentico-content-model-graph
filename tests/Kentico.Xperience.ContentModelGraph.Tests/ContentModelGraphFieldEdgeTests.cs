using System.Xml.Linq;

namespace Kentico.Xperience.ContentModelGraph.Tests;

public class ContentModelGraphFieldEdgeTests
{
    private static readonly Guid articlePageGuid = Guid.Parse("9b5e6c86-ebd1-4471-8f5d-dca72e39b12b");
    private static readonly Guid productPageGuid = Guid.Parse("f943db37-2aa0-41aa-9a47-3810a9c6d2cf");

    private static readonly Dictionary<Guid, string> classNamesByGuid = new()
    {
        [articlePageGuid] = "DancingGoat.ArticlePage",
        [productPageGuid] = "DancingGoat.ProductPage"
    };

    [Test]
    public void CreateFieldEdges_KeepsAReferenceToTheFieldsOwnClass()
    {
        var edges = CreateArticleEdges(ReferenceField("ArticleRelatedPages", "Related pages", articlePageGuid, productPageGuid));

        Assert.That(
            edges.Select(edge => (edge.Source, edge.Target, edge.Kind, edge.Label)),
            Is.EquivalentTo(new[]
            {
                ("class:dancinggoat.articlepage", "class:dancinggoat.articlepage", "contentReference", "Related pages"),
                ("class:dancinggoat.articlepage", "class:dancinggoat.productpage", "contentReference", "Related pages")
            }));
    }

    [Test]
    public void CreateFieldEdges_MergesSelfReferencingFieldsIntoOneEdge()
    {
        var edges = CreateArticleEdges(
            ReferenceField("ArticleRelatedPages", "Related pages", articlePageGuid),
            ReferenceField("ArticleNextPage", "Next page", articlePageGuid));

        Assert.Multiple(() =>
        {
            Assert.That(edges, Has.Count.EqualTo(1));
            Assert.That(edges[0].Source, Is.EqualTo(edges[0].Target));
            Assert.That(edges[0].Label, Is.EqualTo("Next page, Related pages"));
        });
    }

    [Test]
    public void CreateFieldEdges_KeepsAnObjectReferenceToTheFieldsOwnClass()
    {
        var field = XElement.Parse("""<field column="ParentID" refobjtype="DancingGoat.ArticlePage" />""");

        var edges = CreateArticleEdges(field);

        Assert.That(
            edges.Select(edge => (edge.Source, edge.Target, edge.Kind)),
            Is.EqualTo(new[] { ("class:dancinggoat.articlepage", "class:dancinggoat.articlepage", "objectReference") }));
    }

    private static IReadOnlyList<GraphEdge> CreateArticleEdges(params XElement[] fields) =>
        ContentModelGraphBuilder.CreateFieldEdges(fields, "DancingGoat.ArticlePage", classNamesByGuid, new Dictionary<Guid, string>(), new Dictionary<Guid, string>(), false);

    private static XElement ReferenceField(string column, string caption, params Guid[] allowedTypes) => new(
        "field",
        new XAttribute("column", column),
        new XAttribute("columntype", "contentitemreference"),
        new XElement("properties", new XElement("fieldcaption", caption)),
        new XElement(
            "settings",
            new XElement("AllowedContentItemTypeIdentifiers", $"[{string.Join(",", allowedTypes.Select(guid => $"\"{guid}\""))}]")));
}
