using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kentico.Xperience.Labs.ContentModelGraph;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddContentModelGraph(this IServiceCollection services)
    {
        services.TryAddSingleton<IContentModelGraphBuilder, ContentModelGraphBuilder>();

        return services;
    }
}
