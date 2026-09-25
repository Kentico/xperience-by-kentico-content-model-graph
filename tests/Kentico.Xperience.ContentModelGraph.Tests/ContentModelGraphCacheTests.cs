namespace Kentico.Xperience.ContentModelGraph.Tests;

/// <summary>
/// The content model graph is cached for an hour and carries administration links that depend on the
/// viewer's application access, so the cache is keyed by that access. These pin the property the whole
/// arrangement rests on: two users with different access can never name the same cache entry, and so can
/// never be served each other's links.
/// </summary>
public class ContentModelGraphCacheTests
{
    private static IEnumerable<GraphApplicationAccess> AllAccessCombinations()
    {
        foreach (bool contentTypes in new[] { true, false })
        {
            foreach (bool taxonomy in new[] { true, false })
            {
                foreach (bool forms in new[] { true, false })
                {
                    foreach (bool modules in new[] { true, false })
                    {
                        yield return new GraphApplicationAccess(contentTypes, taxonomy, forms, modules);
                    }
                }
            }
        }
    }

    // CacheSettings joins the name parts into the cache item name, so distinct parts are distinct entries.
    private static string GetCacheItemName(GraphApplicationAccess applications) =>
        string.Join('|', ContentModelGraphCache.GetItemNameParts(applications));

    // Four applications, so sixteen combinations - the count is asserted alongside uniqueness so that a
    // fifth flag added without a cache key part fails here rather than quietly sharing an entry.
    [Test]
    public void GetItemNameParts_GivesEveryApplicationAccessItsOwnCacheEntry()
    {
        string[] names = [.. AllAccessCombinations().Select(GetCacheItemName)];

        Assert.Multiple(() =>
        {
            Assert.That(names, Has.Length.EqualTo(16));
            Assert.That(names, Is.Unique);
        });
    }

    // The Modules flag is the newest, and the one a stale cache key would drop first: two users differing
    // only in Modules access must not share the object type links the other may not follow.
    [Test]
    public void GetItemNameParts_SeparatesUsersWhoDifferOnlyInModulesAccess() => Assert.That(
        GetCacheItemName(new GraphApplicationAccess(ContentTypes: true, Taxonomy: true, Forms: true, Modules: false)),
        Is.Not.EqualTo(GetCacheItemName(GraphApplicationAccess.All)));

    [Test]
    public void GetItemNameParts_GivesUsersWithTheSameAccessTheSameCacheEntry() => Assert.That(
        GetCacheItemName(new GraphApplicationAccess(ContentTypes: true, Taxonomy: false, Forms: true, Modules: false)),
        Is.EqualTo(GetCacheItemName(new GraphApplicationAccess(ContentTypes: true, Taxonomy: false, Forms: true, Modules: false))));

    // An administrator sees every link. Nobody with a denial anywhere may land on that entry.
    [Test]
    public void GetItemNameParts_DoesNotShareTheAdministratorEntryWithAnyDeniedUser()
    {
        string administrator = GetCacheItemName(GraphApplicationAccess.All);
        string[] denied = [.. AllAccessCombinations()
            .Where(access => access != GraphApplicationAccess.All)
            .Select(GetCacheItemName)];

        Assert.That(denied, Has.No.Member(administrator));
    }

    // Every variant hangs off the one dependency key, so "Clear cache" clears all of them rather than the
    // caller's own view of the graph.
    [Test]
    public void GetItemNameParts_StartWithTheSharedDependencyKey() =>
        Assert.That(
            AllAccessCombinations().Select(access => ContentModelGraphCache.GetItemNameParts(access)[0]),
            Is.All.EqualTo(ContentModelGraphCache.DEPENDENCY_KEY));
}
