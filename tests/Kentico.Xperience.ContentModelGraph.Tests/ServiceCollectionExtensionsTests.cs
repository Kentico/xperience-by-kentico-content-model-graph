using Microsoft.Extensions.DependencyInjection;

namespace Kentico.Xperience.ContentModelGraph.Tests;

public class ServiceCollectionExtensionsTests
{
    [Test]
    public void AddContentModelGraph_RegistersBuildersOnce()
    {
        var services = new ServiceCollection();

        services.AddContentModelGraph();
        services.AddContentModelGraph();

        var graphDescriptor = services.Single(service => service.ServiceType == typeof(IContentModelGraphBuilder));
        var relationshipDescriptor = services.Single(service => service.ServiceType == typeof(IContentItemRelationshipGraphBuilder));
        var permissionDescriptor = services.Single(service => service.ServiceType == typeof(IContentItemGraphPermissionEvaluator));

        Assert.Multiple(() =>
        {
            // Scoped, not singleton - the evaluator resolves the authenticated user of the current request.
            Assert.That(permissionDescriptor.ImplementationType, Is.EqualTo(typeof(ContentItemGraphPermissionEvaluator)));
            Assert.That(permissionDescriptor.Lifetime, Is.EqualTo(ServiceLifetime.Scoped));
            Assert.That(graphDescriptor.ImplementationType, Is.EqualTo(typeof(ContentModelGraphBuilder)));
            Assert.That(graphDescriptor.Lifetime, Is.EqualTo(ServiceLifetime.Singleton));
            Assert.That(relationshipDescriptor.ImplementationType, Is.EqualTo(typeof(ContentItemRelationshipGraphBuilder)));
            Assert.That(relationshipDescriptor.Lifetime, Is.EqualTo(ServiceLifetime.Scoped));
        });
    }
}
