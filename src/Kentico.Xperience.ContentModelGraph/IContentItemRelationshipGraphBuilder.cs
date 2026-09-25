namespace Kentico.Xperience.ContentModelGraph;

public interface IContentItemRelationshipGraphBuilder
{
    public Task<ContentItemRelationshipGraph> Build(int itemId, int contentLanguageId);

    /// <summary>
    /// Builds the graph rooted at a form rather than at a content item: the form itself plus the content
    /// items whose latest Page Builder configuration embeds it. A form has no outgoing relationships of its
    /// own, so the outgoing direction is always empty.
    /// </summary>
    public Task<ContentItemRelationshipGraph> BuildForForm(int formId);
}
