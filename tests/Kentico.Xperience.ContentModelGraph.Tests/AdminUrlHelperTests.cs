namespace Kentico.Xperience.ContentModelGraph.Tests;

public class AdminUrlHelperTests
{
    [TestCase("/content-types/list/6665/fields", "/admin/content-types/list/6665/fields")]
    [TestCase("content-types/list/6665/fields", "/admin/content-types/list/6665/fields")]
    [TestCase("/admin/content-types/list/6665/fields", "/admin/content-types/list/6665/fields")]
    [TestCase("/ADMIN/content-types/list/6665/fields", "/ADMIN/content-types/list/6665/fields")]
    [TestCase("/admin", "/admin")]
    public void EnsureAdminPrefix_ReturnsAdminRootedPath(string path, string expected) => Assert.That(AdminUrlHelper.EnsureAdminPrefix(path), Is.EqualTo(expected));
}
