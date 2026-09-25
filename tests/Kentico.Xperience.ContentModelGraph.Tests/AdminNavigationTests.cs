using Kentico.Xperience.Admin.Base;

namespace Kentico.Xperience.ContentModelGraph.Tests;

public class AdminNavigationTests
{
    [Test]
    public void MoveToEnd_PutsTheMatchingItemLastAndKeepsTheRestInOrder()
    {
        var items = Navigation("general", "content-model-graph", "allowed-channels", "allowed-scopes");

        var result = AdminNavigation.MoveToEnd(items, "content-model-graph").ToList();

        Assert.That(
            result.Select(item => item.Path),
            Is.EqualTo(new[] { "general", "allowed-channels", "allowed-scopes", "content-model-graph" }));
    }

    [Test]
    public void MoveToEnd_LeavesTheListUnchangedWhenTheSlugIsAbsent()
    {
        var items = Navigation("general", "fields");

        var result = AdminNavigation.MoveToEnd(items, "content-model-graph").ToList();

        Assert.That(result.Select(item => item.Path), Is.EqualTo(new[] { "general", "fields" }));
    }

    [Test]
    public void MoveToEnd_MatchesTheSlugCaseInsensitively()
    {
        var items = Navigation("Content-Model-Graph", "general");

        var result = AdminNavigation.MoveToEnd(items, "content-model-graph").ToList();

        Assert.That(result.Select(item => item.Path), Is.EqualTo(new[] { "general", "Content-Model-Graph" }));
    }

    [Test]
    public void Remove_DropsOnlyTheMatchingItem()
    {
        var items = Navigation("relationships", "root-general", "root-properties");

        var result = AdminNavigation.Remove(items, "relationships").ToList();

        Assert.That(result.Select(item => item.Path), Is.EqualTo(new[] { "root-general", "root-properties" }));
    }

    [Test]
    public void Remove_LeavesTheListUnchangedWhenTheSlugIsAbsent()
    {
        var items = Navigation("root-general", "root-properties");

        var result = AdminNavigation.Remove(items, "relationships").ToList();

        Assert.That(result.Select(item => item.Path), Is.EqualTo(new[] { "root-general", "root-properties" }));
    }

    private static List<NavigationItem> Navigation(params string[] slugs) =>
        [.. slugs.Select(slug => new NavigationItem { Path = slug, Label = slug })];
}
