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

        Assert.Multiple(() =>
        {
            Assert.That(graphDescriptor.ImplementationType, Is.EqualTo(typeof(ContentModelGraphBuilder)));
            Assert.That(graphDescriptor.Lifetime, Is.EqualTo(ServiceLifetime.Singleton));
            Assert.That(relationshipDescriptor.ImplementationType, Is.EqualTo(typeof(ContentItemRelationshipGraphBuilder)));
            Assert.That(relationshipDescriptor.Lifetime, Is.EqualTo(ServiceLifetime.Singleton));
        });
    }
}
