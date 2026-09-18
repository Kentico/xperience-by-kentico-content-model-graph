namespace Kentico.Xperience.ContentModelGraph;

public interface IContentItemRelationshipGraphBuilder
{
    public Task<ContentItemRelationshipGraph> Build(int itemId, int contentLanguageId);
}
