using System.Reflection;

using CMS.Membership;

using Kentico.Xperience.Admin.Base;

namespace Kentico.Xperience.ContentModelGraph.Tests;

/// <summary>
/// The standalone application is gated by the platform, not by the page: every administration page load
/// runs an application "View" check before the page is even constructed. What the attribute adds is the
/// permission an administrator can grant - without it the check has nothing to match and only
/// administrators get in. These pin the declaration, because losing it is silent: the application keeps
/// working for administrators and simply disappears from Role management.
/// </summary>
public class ContentModelGraphApplicationPermissionTests
{
    private static string[] GetDeclaredPermissionNames(Type applicationType) =>
        [.. applicationType.GetCustomAttributes<UIPermissionAttribute>().Select(permission => permission.Name)];

    // The permission has to sit on the type the UIApplication registration names. The platform reads the
    // attributes of exactly that type, so moving the declaration to the base class would register nothing.
    [Test]
    public void RegisteredApplicationType_IsTheTypeCarryingThePermission()
    {
        var registrations = typeof(ContentModelGraphPage).Assembly.GetCustomAttributes<UIApplicationAttribute>();
        var registration = registrations.Single(application => application.Identifier == ContentModelGraphPage.IDENTIFIER);

        Assert.That(GetDeclaredPermissionNames(registration.Type), Is.EqualTo(new[] { SystemPermissions.VIEW }));
    }

    // "View" is the permission the platform demands of an application page, so it is the one that must be
    // grantable. The label is a platform resource string, which is what localizes it in Role management.
    [Test]
    public void ContentModelGraphPage_DeclaresTheViewPermission()
    {
        var permissions = typeof(ContentModelGraphPage).GetCustomAttributes<UIPermissionAttribute>();
        var view = permissions.SingleOrDefault(permission => permission.Name == SystemPermissions.VIEW);

        Assert.That(view?.DisplayName, Is.EqualTo("{$base.roles.permissions.view$}"));
    }

    // The graph is read-only. Create, Update, and Delete would be switches in Role management that govern
    // nothing, so View is deliberately the only one.
    [Test]
    public void ContentModelGraphPage_DeclaresNoPermissionBesidesView() => Assert.That(
        GetDeclaredPermissionNames(typeof(ContentModelGraphPage)),
        Is.EqualTo(new[] { SystemPermissions.VIEW }));

    // NoPermissionRequired turns the platform's check off outright. Adding it alongside the permission is
    // rejected at startup, but adding it in place of one would quietly reopen the application to everyone.
    [Test]
    public void ContentModelGraphPage_DoesNotOptOutOfPermissionEvaluation() => Assert.That(
        typeof(ContentModelGraphPage).GetCustomAttribute<NoPermissionRequiredAttribute>(),
        Is.Null);
}
