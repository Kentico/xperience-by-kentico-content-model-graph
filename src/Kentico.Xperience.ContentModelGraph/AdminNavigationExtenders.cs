using CMS.Websites.Internal;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.UIPages;
using Kentico.Xperience.Admin.Websites.UIPages;
using Kentico.Xperience.ContentModelGraph;

[assembly: PageExtender(typeof(ContentTypeEditSectionNavigationExtender))]
[assembly: PageExtender(typeof(WebPageLayoutNavigationExtender))]

namespace Kentico.Xperience.ContentModelGraph;

/// <summary>
/// The slugs this module registers its pages under. Shared with the navigation extenders below, which have to
/// find those pages again in the already-built navigation of a host page.
/// </summary>
internal static class ContentModelGraphSlugs
{
    public const string ContentModelGraph = "content-model-graph";

    public const string Relationships = "relationships";
}

/// <summary>
/// Puts "Content model graph" at the end of a content type's navigation.
/// </summary>
/// <remarks>
/// <para>
/// A page order cannot do this. "Allowed in channels", "Allowed in scopes", "Allowed content types" and
/// "Allowed email templates" are all registered with <c>[UINavigation(false)]</c>, so
/// <c>Page&lt;T&gt;.ConfigureNavigation</c> leaves them out of the ordered navigation entirely.
/// <c>ContentTypeEditSection.ConfigureTemplateProperties</c> then appends them by hand, conditionally, after
/// every order-driven item - see its <c>AddConditionalNavigation</c>. Nothing ordered can therefore sort after
/// them, whatever its order value.
/// </para>
/// <para>
/// Extenders run after the host page has configured its template properties, so this is the only seam that
/// sees the final list. A page can carry any number of extenders (<c>UITreeNode.Extenders</c> is a set of every
/// registered <see cref="PageExtender{TPage}" /> whose page type is assignable from the node's), so this neither
/// replaces nor blocks an extender registered by the host application or another library.
/// </para>
/// </remarks>
internal sealed class ContentTypeEditSectionNavigationExtender : PageExtender<ContentTypeEditSection>
{
    public override async Task<TemplateClientProperties> ConfigureTemplateProperties(TemplateClientProperties properties)
    {
        properties = await base.ConfigureTemplateProperties(properties);

        properties.Navigation.Items = AdminNavigation.MoveToEnd(
            properties.Navigation.Items,
            ContentModelGraphSlugs.ContentModelGraph);

        return properties;
    }
}

/// <summary>
/// Hides "Content relationships" on a website channel root.
/// </summary>
/// <remarks>
/// <para>
/// The root of the content tree is synthetic - <see cref="WebPageConstants.ROOT_NODE_ID" />, no web page item and
/// no content item behind it - so the relationship graph can only ever report that the object does not exist.
/// The platform hides its own tabs there the same way, in
/// <c>WebPageLayout.ConfigureTemplateProperties</c>, but that list is fixed and closed to us.
/// </para>
/// <para>
/// Only the navigation entry is removed; the route stays registered, exactly as with the tabs the platform
/// removes on the root. This touches one item, identified by this module's own slug, and leaves every other
/// item and every other extender alone.
/// </para>
/// </remarks>
internal sealed class WebPageLayoutNavigationExtender : PageExtender<WebPageLayout>
{
    public override async Task<TemplateClientProperties> ConfigureTemplateProperties(TemplateClientProperties properties)
    {
        properties = await base.ConfigureTemplateProperties(properties);

        if (Page.WebPageIdentifier?.WebPageItemID == WebPageConstants.ROOT_NODE_ID)
        {
            properties.Navigation.Items = AdminNavigation.Remove(
                properties.Navigation.Items,
                ContentModelGraphSlugs.Relationships);
        }

        return properties;
    }
}

/// <summary>
/// Navigation list edits shared by the extenders above.
/// </summary>
internal static class AdminNavigation
{
    /// <summary>
    /// Returns <paramref name="items" /> with the item at <paramref name="slug" /> moved to the end, keeping the
    /// relative order of everything else. A slug that is not present leaves the list unchanged.
    /// </summary>
    public static IEnumerable<NavigationItem> MoveToEnd(IEnumerable<NavigationItem> items, string slug)
    {
        var all = items.ToList();
        var moved = all.Where(item => HasSlug(item, slug)).ToList();

        if (moved.Count == 0)
        {
            return all;
        }

        return all.Where(item => !HasSlug(item, slug)).Concat(moved).ToList();
    }

    /// <summary>
    /// Returns <paramref name="items" /> without the item at <paramref name="slug" />.
    /// </summary>
    public static IEnumerable<NavigationItem> Remove(IEnumerable<NavigationItem> items, string slug) =>
        items.Where(item => !HasSlug(item, slug)).ToList();

    private static bool HasSlug(NavigationItem item, string slug) =>
        string.Equals(item.Path, slug, StringComparison.OrdinalIgnoreCase);
}
