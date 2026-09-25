using CMS.ContentEngine;
using CMS.DataEngine;
using CMS.Helpers;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.UIPages;
using Kentico.Xperience.ContentModelGraph;

[assembly: UIPage(
    typeof(ContentTypeEditSection),
    ContentModelGraphSlugs.ContentModelGraph,
    typeof(ContentTypeModelGraphPage),
    "Content model graph",
    "@kentico/xperience-content-model-graph/ContentModelGraph",
    ContentModelGraphPageOrder.Last,
    Icon = Icons.ProjectScheme)]

[assembly: UIPage(
    typeof(ReusableFieldSchemaEditSection),
    ContentModelGraphSlugs.ContentModelGraph,
    typeof(ReusableFieldSchemaModelGraphPage),
    "Content model graph",
    "@kentico/xperience-content-model-graph/ContentModelGraph",
    ContentModelGraphPageOrder.Last,
    Icon = Icons.ProjectScheme)]

[assembly: UIPage(
    typeof(TaxonomyEditSection),
    ContentModelGraphSlugs.ContentModelGraph,
    typeof(TaxonomyModelGraphPage),
    "Content model graph",
    "@kentico/xperience-content-model-graph/ContentModelGraph",
    ContentModelGraphPageOrder.Last,
    Icon = Icons.ProjectScheme)]

namespace Kentico.Xperience.ContentModelGraph;

internal static class ContentModelGraphPageOrder
{
    public const int Last = UIPageOrder.NoOrder + 100;

    /// <summary>
    /// For surfaces that host both of this module's tabs, so "Content relationships" stays immediately to the
    /// left of "Content model graph" - the same order they appear in on content items.
    /// </summary>
    public const int BeforeLast = Last - 1;

    /// <summary>
    /// After every tab the Websites application registers on <c>WebPageLayout</c> - the highest is
    /// "root-properties" at 10100 - so "Content relationships" sorts last on a web page and, should the
    /// navigation extender ever fail to hide it, on a channel root too.
    /// </summary>
    public const int AfterWebPageLayoutTabs = 10200;
}

internal abstract class ContextualContentModelGraphPage(
    IProgressiveCache progressiveCache,
    IContentModelGraphBuilder builder,
    IContentItemGraphPermissionEvaluator permissionEvaluator)
    : ContentModelGraphPageBase(progressiveCache, builder, permissionEvaluator)
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
    IContentModelGraphBuilder builder,
    IContentItemGraphPermissionEvaluator permissionEvaluator)
    : ContextualContentModelGraphPage(progressiveCache, builder, permissionEvaluator)
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
    IContentModelGraphBuilder builder,
    IContentItemGraphPermissionEvaluator permissionEvaluator)
    : ContextualContentModelGraphPage(progressiveCache, builder, permissionEvaluator)
{
    [PageParameter(typeof(GuidPageModelBinder), typeof(ReusableFieldSchemaEditSection))]
    public Guid PageIdentifier { get; set; }

    protected override Task<string?> ResolveNodeId() =>
        Task.FromResult<string?>($"schema:{PageIdentifier}");
}

internal sealed class TaxonomyModelGraphPage(
    IProgressiveCache progressiveCache,
    IContentModelGraphBuilder builder,
    IContentItemGraphPermissionEvaluator permissionEvaluator,
    IInfoProvider<TaxonomyInfo> taxonomyInfoProvider)
    : ContextualContentModelGraphPage(progressiveCache, builder, permissionEvaluator)
{
    [PageParameter(typeof(IntPageModelBinder), typeof(TaxonomyEditSection))]
    public int TaxonomyID { get; set; }

    protected override async Task<string?> ResolveNodeId()
    {
        var taxonomy = (await taxonomyInfoProvider.Get()
            .Columns(nameof(TaxonomyInfo.TaxonomyGUID))
            .WhereEquals(nameof(TaxonomyInfo.TaxonomyID), TaxonomyID)
            .GetEnumerableTypedResultAsync())
            .FirstOrDefault();

        return taxonomy is null ? null : $"taxonomy:{taxonomy.TaxonomyGUID}";
    }
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

        var nodes = graph.Nodes.Where(node => nodeIds.Contains(node.Id)).ToList();

        return new GraphData
        {
            Nodes = nodes,
            Edges = edges,
            // The identifier as the graph itself spells it, rather than as the caller happened to case it,
            // so what reaches the client is one of its own node ids. A page can ask for a node the graph
            // does not contain - a content type excluded from the model - in which case the id is reported
            // as it was asked for and simply matches nothing on the client.
            FocalNodeId = nodes
                .FirstOrDefault(node => string.Equals(node.Id, nodeId, StringComparison.OrdinalIgnoreCase))
                ?.Id ?? nodeId
        };
    }
}
