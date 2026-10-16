using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kentico.Xperience.ContentModelGraph;

[Obsolete(DeprecationMessages.PackageRenamed, DiagnosticId = DeprecationMessages.DiagnosticId, UrlFormat = DeprecationMessages.Url)]
public static class ServiceCollectionExtensions
{
    [Obsolete(DeprecationMessages.PackageRenamed, DiagnosticId = DeprecationMessages.DiagnosticId, UrlFormat = DeprecationMessages.Url)]
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
