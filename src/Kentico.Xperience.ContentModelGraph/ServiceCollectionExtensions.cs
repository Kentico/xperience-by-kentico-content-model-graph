using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kentico.Xperience.ContentModelGraph;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddContentModelGraph(this IServiceCollection services)
    {
        services.TryAddSingleton<IContentModelGraphBuilder, ContentModelGraphBuilder>();

        // Both are scoped rather than singleton: the permission evaluator resolves the authenticated user,
        // which is a per-request service, and the relationship graph builder depends on the evaluator.
        services.TryAddScoped<IContentItemGraphPermissionEvaluator, ContentItemGraphPermissionEvaluator>();
        services.TryAddScoped<IContentItemRelationshipGraphBuilder, ContentItemRelationshipGraphBuilder>();

        return services;
    }
}
