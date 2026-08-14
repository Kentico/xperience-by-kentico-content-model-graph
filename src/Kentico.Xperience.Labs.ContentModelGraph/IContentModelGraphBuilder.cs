namespace Kentico.Xperience.Labs.ContentModelGraph;

public interface IContentModelGraphBuilder
{
    public Task<GraphData> Build();
}
