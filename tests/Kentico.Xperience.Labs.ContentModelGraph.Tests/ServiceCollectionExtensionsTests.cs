using Microsoft.Extensions.DependencyInjection;

namespace Kentico.Xperience.Labs.ContentModelGraph.Tests;

public class ServiceCollectionExtensionsTests
{
    [Test]
    public void AddContentModelGraph_RegistersBuilderOnce()
    {
        var services = new ServiceCollection();

        services.AddContentModelGraph();
        services.AddContentModelGraph();

        var descriptor = services.Single(service => service.ServiceType == typeof(IContentModelGraphBuilder));

        Assert.Multiple(() =>
        {
            Assert.That(descriptor.ImplementationType, Is.EqualTo(typeof(ContentModelGraphBuilder)));
            Assert.That(descriptor.Lifetime, Is.EqualTo(ServiceLifetime.Singleton));
        });
    }
}
