using System.Reflection;

using CMS.Helpers;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.UIPages;

[assembly: UIApplication(
    identifier: Kentico.Xperience.ContentModelGraph.ContentModelGraphPage.IDENTIFIER,
    type: typeof(Kentico.Xperience.ContentModelGraph.ContentModelGraphPage),
    slug: "content-model-graph",
    name: "Content model graph",
    category: BaseApplicationCategories.DEVELOPMENT,
    icon: Icons.CustomElement,
    templateName: "@kentico/xperience-content-model-graph/ContentModelGraph")]

namespace Kentico.Xperience.ContentModelGraph;

internal sealed class ContentModelGraphPage(
    IProgressiveCache progressiveCache,
    IContentModelGraphBuilder builder) : Page<ContentModelGraphClientProperties>
{
    public const string IDENTIFIER = "Kentico.Xperience.ContentModelGraph.Admin.App";

    private const string CACHE_KEY = "kentico|xperience|contentmodelgraph";
    private const int CACHE_MINUTES = 60;

    public override async Task<ContentModelGraphClientProperties> ConfigureTemplateProperties(ContentModelGraphClientProperties properties)
    {
        properties.Graph = await GetGraph();
        properties.AssemblyName = Assembly.GetExecutingAssembly().GetName().Name ?? "content-model-graph";

        return properties;
    }

    [PageCommand]
    public async Task<ICommandResponse<GraphData>> ResetGraph() =>
        ResponseFrom(await GetGraph()).AddSuccessMessage("Content model graph reset.");

    [PageCommand]
    public async Task<ICommandResponse<GraphData>> ClearCache()
    {
        CacheHelper.TouchKey(CACHE_KEY);

        return ResponseFrom(await GetGraph()).AddSuccessMessage("Content model graph cache cleared.");
    }

    private Task<GraphData> GetGraph() =>
        progressiveCache.LoadAsync(_ => builder.Build(), new CacheSettings(CACHE_MINUTES, CACHE_KEY)
        {
            GetCacheDependency = () => CacheHelper.GetCacheDependency([CACHE_KEY])
        });
}

internal sealed class ContentModelGraphClientProperties : TemplateClientProperties
{
    public GraphData Graph { get; set; } = new();

    public string AssemblyName { get; set; } = string.Empty;
}
