namespace Kentico.Xperience.ContentModelGraph;

public interface IContentModelGraphBuilder
{
    /// <summary>
    /// Builds the whole content model graph as a user with the given application access sees it: a node
    /// whose administration application that user cannot open keeps its label and loses its link.
    /// </summary>
    /// <remarks>
    /// Access is a parameter rather than a dependency because this builder is registered as a singleton
    /// (see <see cref="ServiceCollectionExtensions.AddContentModelGraph" />) while application permissions
    /// are per user - holding the access record on the builder would hand one user another user's links.
    /// The caller is also the one that caches the result, and it keys that cache by the same record.
    /// </remarks>
    public Task<GraphData> Build(GraphApplicationAccess applications);
}
