using System.Reflection;

using CMS.Helpers;
using CMS.Membership;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.UIPages;

[assembly: UIApplication(
    identifier: Kentico.Xperience.ContentModelGraph.ContentModelGraphPage.IDENTIFIER,
    type: typeof(Kentico.Xperience.ContentModelGraph.ContentModelGraphPage),
    slug: "content-model-graph",
    name: "Content model graph",
    category: BaseApplicationCategories.DEVELOPMENT,
    icon: Icons.ProjectScheme,
    templateName: "@kentico/xperience-content-model-graph/ContentModelGraph")]

namespace Kentico.Xperience.ContentModelGraph;

/// <summary>
/// The standalone content model graph application. The declared permission is what makes the application
/// assignable in Role management - the platform already demands <see cref="SystemPermissions.VIEW" /> of
/// every application page, so without a declaration there is nothing an administrator can grant and only
/// administrators can open it. Only View is declared because the graph is read-only; Create, Update, or
/// Delete would be switches that govern nothing.
/// </summary>
[UIPermission(SystemPermissions.VIEW, "{$base.roles.permissions.view$}")]
internal sealed class ContentModelGraphPage(
    IProgressiveCache progressiveCache,
    IContentModelGraphBuilder builder,
    IContentItemGraphPermissionEvaluator permissionEvaluator)
    : ContentModelGraphPageBase(progressiveCache, builder, permissionEvaluator)
{
    public const string IDENTIFIER = "Kentico.Xperience.ContentModelGraph.Admin.App";
}

internal abstract class ContentModelGraphPageBase(
    IProgressiveCache progressiveCache,
    IContentModelGraphBuilder builder,
    IContentItemGraphPermissionEvaluator permissionEvaluator) : Page<ContentModelGraphClientProperties>
{
    private const int CACHE_MINUTES = 60;

    protected virtual bool ShowFieldNamesByDefault => true;

    public override async Task<ContentModelGraphClientProperties> ConfigureTemplateProperties(ContentModelGraphClientProperties properties)
    {
        properties.Graph = await GetGraph();
        properties.AssemblyName = Assembly.GetExecutingAssembly().GetName().Name ?? "content-model-graph";
        properties.ShowFieldNamesByDefault = ShowFieldNamesByDefault;

        return properties;
    }

    [PageCommand]
    public async Task<ICommandResponse<GraphData>> ResetGraph() =>
        ResponseFrom(await GetGraph()).AddSuccessMessage("Content model graph reset.");

    [PageCommand]
    public async Task<ICommandResponse<GraphData>> ClearCache()
    {
        CacheHelper.TouchKey(ContentModelGraphCache.DEPENDENCY_KEY);

        return ResponseFrom(await GetGraph()).AddSuccessMessage("Content model graph cache cleared.");
    }

    /// <summary>
    /// The content model graph as the current user may see it. The graph carries administration links, and
    /// which of them are offered depends on the user's application access - so the cache is keyed by that
    /// access and not only by the graph, or the first visitor's links would be served to everyone else for
    /// the next hour. Access is four booleans, so there are at most sixteen entries however many users there
    /// are, and two users with the same access genuinely can share one graph. Every entry still hangs off
    /// the one dependency key, so clearing the cache clears all of them at once.
    /// </summary>
    protected virtual async Task<GraphData> GetGraph()
    {
        var applications = await permissionEvaluator.GetApplicationAccess();

        return await progressiveCache.LoadAsync(
            _ => builder.Build(applications),
            new CacheSettings(CACHE_MINUTES, ContentModelGraphCache.GetItemNameParts(applications))
            {
                GetCacheDependency = () => CacheHelper.GetCacheDependency([ContentModelGraphCache.DEPENDENCY_KEY])
            });
    }
}

/// <summary>
/// How one user's view of the content model graph is named in the cache. Kept out of the page so that the
/// one thing that must never go wrong - two users with different application access sharing a cached graph,
/// and so one user's links - can be asserted without an administration request.
/// </summary>
internal static class ContentModelGraphCache
{
    /// <summary>
    /// Shared by every cached variant of the graph, so a single touch clears them all. It is also the first
    /// part of every cache item name, which keeps the cached entries recognizable.
    /// </summary>
    internal const string DEPENDENCY_KEY = "kentico|xperience|contentmodelgraph";

    internal static object[] GetItemNameParts(GraphApplicationAccess applications) =>
        [DEPENDENCY_KEY, applications.ContentTypes, applications.Taxonomy, applications.Forms, applications.Modules];
}

internal sealed class ContentModelGraphClientProperties : TemplateClientProperties
{
    public GraphData Graph { get; set; } = new();

    public string AssemblyName { get; set; } = string.Empty;

    public bool ShowFieldNamesByDefault { get; set; }
}
