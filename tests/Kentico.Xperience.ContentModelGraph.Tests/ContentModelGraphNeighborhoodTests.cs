namespace Kentico.Xperience.ContentModelGraph.Tests;

public class ContentModelGraphNeighborhoodTests
{
    [Test]
    public void Filter_ReturnsCurrentNodeAndImmediateIncomingAndOutgoingConnections()
    {
        var graph = CreateGraph();

        var result = ContentModelGraphNeighborhood.Filter(graph, "class:article");

        Assert.Multiple(() =>
        {
            Assert.That(result.Nodes.Select(node => node.Id), Is.EquivalentTo(new[]
            {
                "class:article",
                "class:author",
                "schema:product",
                "taxonomy:topics"
            }));
            Assert.That(result.Edges.Select(edge => edge.Id), Is.EquivalentTo(new[]
            {
                "author-article",
                "article-product",
                "article-topics"
            }));
        });
    }

    [Test]
    public void Filter_IsCaseInsensitive()
    {
        var result = ContentModelGraphNeighborhood.Filter(CreateGraph(), "CLASS:ARTICLE");

        Assert.That(result.Nodes, Has.Count.EqualTo(4));
    }

    [Test]
    public void Filter_ReportsFocalNodeIdAsTheGraphSpellsIt()
    {
        var result = ContentModelGraphNeighborhood.Filter(CreateGraph(), "CLASS:ARTICLE");

        Assert.That(result.FocalNodeId, Is.EqualTo("class:article"));
    }

    [Test]
    public void Filter_ReportsFocalNodeIdForANodeMissingFromTheGraph()
    {
        var result = ContentModelGraphNeighborhood.Filter(CreateGraph(), "class:excluded");

        Assert.Multiple(() =>
        {
            Assert.That(result.Nodes, Is.Empty);
            Assert.That(result.FocalNodeId, Is.EqualTo("class:excluded"));
        });
    }

    [Test]
    public void GraphData_HasNoFocalNodeByDefault() =>
        Assert.That(new GraphData().FocalNodeId, Is.Null);

    [Test]
    public void Filter_ReturnsIsolatedCurrentNode()
    {
        var result = ContentModelGraphNeighborhood.Filter(CreateGraph(), "class:isolated");

        Assert.Multiple(() =>
        {
            Assert.That(result.Nodes.Select(node => node.Id), Is.EqualTo(new[] { "class:isolated" }));
            Assert.That(result.Edges, Is.Empty);
        });
    }

    private static GraphData CreateGraph() => new()
    {
        Nodes =
        [
            Node("class:article"),
            Node("class:author"),
            Node("schema:product"),
            Node("taxonomy:topics"),
            Node("class:unrelated"),
            Node("class:isolated")
        ],
        Edges =
        [
            Edge("author-article", "class:author", "class:article"),
            Edge("article-product", "class:article", "schema:product"),
            Edge("article-topics", "class:article", "taxonomy:topics"),
            Edge("unrelated-product", "class:unrelated", "schema:product")
        ]
    };

    private static GraphNode Node(string id) => new() { Id = id };

    private static GraphEdge Edge(string id, string source, string target) => new()
    {
        Id = id,
        Source = source,
        Target = target
    };
}
