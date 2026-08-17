namespace Kentico.Xperience.ContentModelGraph;

public interface IContentModelGraphBuilder
{
    public Task<GraphData> Build();
}
