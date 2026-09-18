using CMS.DataEngine;
using CMS.Helpers;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.UIPages;
using Kentico.Xperience.ContentModelGraph;

[assembly: UIPage(
    typeof(ContentTypeEditSection),
    "content-model-graph",
    typeof(ContentTypeModelGraphPage),
    "Content model graph",
    "@kentico/xperience-content-model-graph/ContentModelGraph",
    ContentModelGraphPageOrder.Last,
    Icon = Icons.CustomElement)]

[assembly: UIPage(
    typeof(ReusableFieldSchemaEditSection),
    "content-model-graph",
    typeof(ReusableFieldSchemaModelGraphPage),
    "Content model graph",
    "@kentico/xperience-content-model-graph/ContentModelGraph",
    ContentModelGraphPageOrder.Last,
    Icon = Icons.CustomElement)]

namespace Kentico.Xperience.ContentModelGraph;

internal static class ContentModelGraphPageOrder
{
    public const int Last = UIPageOrder.NoOrder + 100;
}

internal abstract class ContextualContentModelGraphPage(
    IProgressiveCache progressiveCache,
    IContentModelGraphBuilder builder) : ContentModelGraphPageBase(progressiveCache, builder)
{
    protected override bool ShowFieldNamesByDefault => true;

    protected abstract Task<string?> ResolveNodeId();

    protected override async Task<GraphData> GetGraph()
    {
        var graph = await base.GetGraph();
        string? nodeId = await ResolveNodeId();

        return nodeId is null ? new GraphData() : ContentModelGraphNeighborhood.Filter(graph, nodeId);
    }
}

internal sealed class ContentTypeModelGraphPage(
    IProgressiveCache progressiveCache,
    IContentModelGraphBuilder builder) : ContextualContentModelGraphPage(progressiveCache, builder)
{
    [PageParameter(typeof(IntPageModelBinder), typeof(ContentTypeEditSection))]
    public int ObjectId { get; set; }

    protected override async Task<string?> ResolveNodeId()
    {
        var dataClass = (await DataClassInfoProvider.ProviderObject.Get()
            .Columns(nameof(DataClassInfo.ClassName))
            .WhereEquals(nameof(DataClassInfo.ClassID), ObjectId)
            .GetEnumerableTypedResultAsync())
            .FirstOrDefault();

        return dataClass is null ? null : $"class:{dataClass.ClassName.ToLowerInvariant()}";
    }
}

internal sealed class ReusableFieldSchemaModelGraphPage(
    IProgressiveCache progressiveCache,
    IContentModelGraphBuilder builder) : ContextualContentModelGraphPage(progressiveCache, builder)
{
    [PageParameter(typeof(GuidPageModelBinder), typeof(ReusableFieldSchemaEditSection))]
    public Guid PageIdentifier { get; set; }

    protected override Task<string?> ResolveNodeId() =>
        Task.FromResult<string?>($"schema:{PageIdentifier}");
}

internal static class ContentModelGraphNeighborhood
{
    public static GraphData Filter(GraphData graph, string nodeId)
    {
        var edges = graph.Edges
            .Where(edge => string.Equals(edge.Source, nodeId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(edge.Target, nodeId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var nodeIds = edges
            .SelectMany(edge => new[] { edge.Source, edge.Target })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        nodeIds.Add(nodeId);

        return new GraphData
        {
            Nodes = graph.Nodes.Where(node => nodeIds.Contains(node.Id)).ToList(),
            Edges = edges
        };
    }
}
