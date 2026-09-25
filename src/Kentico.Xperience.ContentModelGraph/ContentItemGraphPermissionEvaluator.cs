using CMS.ContentEngine;
using CMS.ContentEngine.Internal;
using CMS.DataEngine;
using CMS.EmailLibrary;
using CMS.Headless;
using CMS.Membership;
using CMS.Membership.Internal;
using CMS.Websites;
using CMS.Websites.Internal;
using CMS.Workspaces;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.Authentication;
using Kentico.Xperience.Admin.Base.UIPages;
using Kentico.Xperience.Admin.DigitalMarketing.UIPages;
using Kentico.Xperience.Admin.Headless.UIPages;

namespace Kentico.Xperience.ContentModelGraph;

/// <summary>
/// Decides which content items the current administration user is allowed to see in the relationship graph.
/// </summary>
public interface IContentItemGraphPermissionEvaluator
{
    /// <summary>
    /// Returns the subset of <paramref name="contentItemIds" /> the current user may view. Identifiers of items
    /// that no longer exist are not returned. The whole set is evaluated in a fixed number of queries, so the
    /// cost does not grow with the number of graph nodes.
    /// </summary>
    public Task<IReadOnlySet<int>> GetViewableItemIds(IReadOnlyCollection<int> contentItemIds);

    /// <summary>
    /// Whether the current user may open the administration applications a graph links out to. Four checks,
    /// evaluated once and reused for the whole request - a graph holds many content type, tag and object
    /// type nodes, and they all link into the same four applications.
    /// </summary>
    public Task<GraphApplicationAccess> GetApplicationAccess();
}

/// <summary>
/// Whether the current user may open each administration application the relationship graph links out to.
/// A denied application costs the node its link, not its label: the content type, tag or form name is still
/// rendered, as plain text.
/// </summary>
/// <remarks>
/// These are static applications, so each one is identified by a plain constant - unlike the per-channel
/// applications <see cref="ContentItemGraphPermissionEvaluator.GetChannelApplicationName" /> composes.
/// </remarks>
/// <param name="ContentTypes">The Content types application, linked to by every content item node.</param>
/// <param name="Taxonomy">The Taxonomy application, linked to by tag nodes.</param>
/// <param name="Forms">The Forms application, linked to by form nodes - both the form nodes of the
/// relationships graph and the form classes of the content model graph, which are authored there and not
/// in Modules.</param>
/// <param name="Modules">The Modules application, linked to by the content model graph's object type
/// nodes - every class that is neither a content type nor a form. Access to the application is the whole
/// check: the individual class definitions inside it are not separately gated.</param>
public sealed record GraphApplicationAccess(bool ContentTypes, bool Taxonomy, bool Forms, bool Modules)
{
    /// <summary>Every application open - what an administrator gets, and the default for a graph built outside a request.</summary>
    public static GraphApplicationAccess All { get; } = new(ContentTypes: true, Taxonomy: true, Forms: true, Modules: true);
}

/// <summary>
/// Evaluates view access per content item surface:
/// <list type="bullet">
/// <item>reusable items - Content hub workspace <c>View</c>,</item>
/// <item>web pages - website channel application <c>View</c> plus a web page ACL <c>Read</c> grant, or the
/// channel-wide <c>ManagePermissions</c>,</item>
/// <item>emails and headless items - the channel application <c>View</c> (neither surface has an ACL layer).</item>
/// </list>
/// Administrators bypass all of it.
/// </summary>
/// <remarks>
/// Uses <c>CMS.Websites.Internal</c> and <c>CMS.Membership.Internal</c> deliberately - there is no public API
/// that evaluates web page ACLs, and evaluating only the Content hub workspace permission (what this page did
/// before) both denies channel editors and exposes channel content to Content hub-only users.
/// </remarks>
public sealed class ContentItemGraphPermissionEvaluator(
    IAuthenticatedUserAccessor authenticatedUserAccessor,
    IApplicationPermissionEvaluator applicationPermissionEvaluator,
    IWorkspacePermissionEvaluator workspacePermissionEvaluator,
    IObjectQueryAclPermissionExtender objectQueryAclPermissionExtender,
    IInfoProvider<ContentItemInfo> contentItemInfoProvider,
    IInfoProvider<ChannelInfo> channelInfoProvider,
    IInfoProvider<WebPageItemInfo> webPageItemInfoProvider,
    IInfoProvider<WebPageAclMappingInfo> webPageAclMappingInfoProvider,
    IInfoProvider<WebsiteChannelInfo> websiteChannelInfoProvider,
    IInfoProvider<EmailChannelInfo> emailChannelInfoProvider,
    IInfoProvider<HeadlessChannelInfo> headlessChannelInfoProvider)
    : IContentItemGraphPermissionEvaluator
{
    /// <summary>
    /// Channel applications are registered per channel under <c>&lt;application identifier&gt;_&lt;channel object GUID&gt;</c>.
    /// The convention is not part of the public API - it was read from the platform's own permission checks
    /// (<c>Kentico.Xperience.Admin.Websites</c>/<c>.DigitalMarketing</c>/<c>.Headless</c>, 31.7.3), so
    /// <see cref="ContentItemGraphPermissionEvaluatorConstants" /> records the expected prefixes for the guard test.
    /// </summary>
    internal static string GetChannelApplicationName(string applicationIdentifier, Guid channelObjectGuid) =>
        $"{applicationIdentifier}_{channelObjectGuid}";

    /// <summary>
    /// Held for the lifetime of the evaluator, which is registered scoped - so the four application checks
    /// happen once per request no matter how many graphs, nodes or expansion commands ask for them.
    /// </summary>
    private GraphApplicationAccess? applicationAccess;

    public async Task<GraphApplicationAccess> GetApplicationAccess()
    {
        if (applicationAccess is not null)
        {
            return applicationAccess;
        }

        var user = await authenticatedUserAccessor.Get();
        applicationAccess = user.IsAdministrator()
            ? GraphApplicationAccess.All
            : new GraphApplicationAccess(
                ContentTypes: HasApplicationPermission(user, ContentTypesApplication.IDENTIFIER, SystemPermissions.VIEW),
                Taxonomy: HasApplicationPermission(user, TaxonomyApplication.IDENTIFIER, SystemPermissions.VIEW),
                Forms: HasApplicationPermission(user, FormsApplication.IDENTIFIER, SystemPermissions.VIEW),
                Modules: HasApplicationPermission(user, ModulesApplication.IDENTIFIER, SystemPermissions.VIEW));

        return applicationAccess;
    }

    public async Task<IReadOnlySet<int>> GetViewableItemIds(IReadOnlyCollection<int> contentItemIds)
    {
        var viewable = new HashSet<int>();
        if (contentItemIds.Count == 0)
        {
            return viewable;
        }

        int[] requestedIds = [.. contentItemIds.Distinct()];
        var items = (await contentItemInfoProvider.Get()
            .Columns(
                nameof(ContentItemInfo.ContentItemID),
                nameof(ContentItemInfo.ContentItemChannelID),
                nameof(ContentItemInfo.ContentItemWorkspaceID))
            .WhereIn(nameof(ContentItemInfo.ContentItemID), requestedIds)
            .GetEnumerableTypedResultAsync())
            .ToArray();
        if (items.Length == 0)
        {
            return viewable;
        }

        var user = await authenticatedUserAccessor.Get();
        if (user.IsAdministrator())
        {
            foreach (var item in items)
            {
                viewable.Add(item.ContentItemID);
            }

            return viewable;
        }

        await AddViewableReusableItems(items, viewable);
        await AddViewableChannelItems(items, user, viewable);

        return viewable;
    }

    // Reusable items carry no channel; their visibility is governed by the Content hub workspace they live in.
    private async Task AddViewableReusableItems(IReadOnlyCollection<ContentItemInfo> items, HashSet<int> viewable)
    {
        foreach (var workspace in items.Where(item => item.ContentItemChannelID <= 0).GroupBy(item => item.ContentItemWorkspaceID))
        {
            if (!(await workspacePermissionEvaluator.Evaluate(
                WorkspaceDataPermissions.VIEW,
                typeof(ContentHubApplication),
                workspace.Key)).Succeeded)
            {
                continue;
            }

            foreach (var item in workspace)
            {
                viewable.Add(item.ContentItemID);
            }
        }
    }

    private async Task AddViewableChannelItems(IReadOnlyCollection<ContentItemInfo> items, UserInfo user, HashSet<int> viewable)
    {
        var itemsByChannel = items
            .Where(item => item.ContentItemChannelID > 0)
            .GroupBy(item => item.ContentItemChannelID)
            .ToDictionary(group => group.Key, group => group.ToArray());
        if (itemsByChannel.Count == 0)
        {
            return;
        }

        int[] channelIds = [.. itemsByChannel.Keys];
        var channels = (await channelInfoProvider.Get()
            .Columns(nameof(ChannelInfo.ChannelID), nameof(ChannelInfo.ChannelType))
            .WhereIn(nameof(ChannelInfo.ChannelID), channelIds)
            .GetEnumerableTypedResultAsync())
            .ToDictionary(channel => channel.ChannelID);
        var applicationNames = await GetChannelApplicationNames(channels.Values);

        // Website channels are resolved last, in one batch, because their ACL check is a single shared query.
        var aclGatedItems = new List<ContentItemInfo>();
        foreach ((int channelId, var channelItems) in itemsByChannel)
        {
            if (!channels.TryGetValue(channelId, out var channel)
                || !applicationNames.TryGetValue(channelId, out string? applicationName))
            {
                continue;
            }

            if (channel.ChannelType == ChannelType.Website
                && !HasApplicationPermission(user, applicationName, WebsiteChannelUIPermissions.MANAGE_PERMISSIONS))
            {
                if (HasApplicationPermission(user, applicationName, SystemPermissions.VIEW))
                {
                    aclGatedItems.AddRange(channelItems);
                }

                continue;
            }

            // ManagePermissions on a website channel overrides its ACLs; emails and headless items have none.
            if (channel.ChannelType == ChannelType.Website
                || HasApplicationPermission(user, applicationName, SystemPermissions.VIEW))
            {
                foreach (var item in channelItems)
                {
                    viewable.Add(item.ContentItemID);
                }
            }
        }

        foreach (int itemId in await GetItemsWithAclReadPermission(aclGatedItems, user))
        {
            viewable.Add(itemId);
        }
    }

    private async Task<IReadOnlyDictionary<int, string>> GetChannelApplicationNames(IReadOnlyCollection<ChannelInfo> channels)
    {
        var names = new Dictionary<int, string>();
        int[] websiteChannelIds = [.. GetChannelIds(channels, ChannelType.Website)];
        int[] emailChannelIds = [.. GetChannelIds(channels, ChannelType.Email)];
        int[] headlessChannelIds = [.. GetChannelIds(channels, ChannelType.Headless)];

        if (websiteChannelIds.Length > 0)
        {
            foreach (var channel in await websiteChannelInfoProvider.Get()
                .Columns(nameof(WebsiteChannelInfo.WebsiteChannelChannelID), nameof(WebsiteChannelInfo.WebsiteChannelGUID))
                .WhereIn(nameof(WebsiteChannelInfo.WebsiteChannelChannelID), websiteChannelIds)
                .GetEnumerableTypedResultAsync())
            {
                names[channel.WebsiteChannelChannelID] = GetChannelApplicationName(
                    WebsiteConstants.WEBSITE_CHANNEL_APPLICATION_PREFIX,
                    channel.WebsiteChannelGUID);
            }
        }

        if (emailChannelIds.Length > 0)
        {
            foreach (var channel in await emailChannelInfoProvider.Get()
                .Columns(nameof(EmailChannelInfo.EmailChannelChannelID), nameof(EmailChannelInfo.EmailChannelGUID))
                .WhereIn(nameof(EmailChannelInfo.EmailChannelChannelID), emailChannelIds)
                .GetEnumerableTypedResultAsync())
            {
                names[channel.EmailChannelChannelID] = GetChannelApplicationName(
                    EmailChannelApplication.IDENTIFIER,
                    channel.EmailChannelGUID);
            }
        }

        if (headlessChannelIds.Length > 0)
        {
            foreach (var channel in await headlessChannelInfoProvider.Get()
                .Columns(nameof(HeadlessChannelInfo.HeadlessChannelChannelID), nameof(HeadlessChannelInfo.HeadlessChannelGUID))
                .WhereIn(nameof(HeadlessChannelInfo.HeadlessChannelChannelID), headlessChannelIds)
                .GetEnumerableTypedResultAsync())
            {
                names[channel.HeadlessChannelChannelID] = GetChannelApplicationName(
                    HeadlessChannelApplication.IDENTIFIER,
                    channel.HeadlessChannelGUID);
            }
        }

        return names;
    }

    private static IEnumerable<int> GetChannelIds(IEnumerable<ChannelInfo> channels, ChannelType channelType) =>
        channels.Where(channel => channel.ChannelType == channelType).Select(channel => channel.ChannelID);

    private bool HasApplicationPermission(UserInfo user, string applicationName, string permissionName) =>
        applicationPermissionEvaluator.Evaluate(new ApplicationPermissionEvaluationContext
        {
            User = user,
            ApplicationName = applicationName,
            PermissionName = permissionName
        });

    /// <summary>
    /// Resolves the web page ACL <c>Read</c> grants for the given items in a single query. ACL inheritance is
    /// already materialized in the mapping table (one effective ACL per page), and web page ACL rows only ever
    /// grant - there is no deny bit - so an inner join over the current user's roles is the whole check.
    /// </summary>
    private async Task<IReadOnlyCollection<int>> GetItemsWithAclReadPermission(IReadOnlyCollection<ContentItemInfo> items, UserInfo user)
    {
        if (items.Count == 0)
        {
            return [];
        }

        int[] contentItemIds = [.. items.Select(item => item.ContentItemID)];
        var webPageItems = (await webPageItemInfoProvider.Get()
            .Columns(nameof(WebPageItemInfo.WebPageItemID), nameof(WebPageItemInfo.WebPageItemContentItemID))
            .WhereIn(nameof(WebPageItemInfo.WebPageItemContentItemID), contentItemIds)
            .GetEnumerableTypedResultAsync())
            .ToArray();
        if (webPageItems.Length == 0)
        {
            return [];
        }

        var baseQuery = webPageAclMappingInfoProvider.Get()
            .WhereIn(nameof(WebPageAclMappingInfo.WebPageAclMappingWebPageItemID), webPageItems.Select(page => page.WebPageItemID).ToArray());
        var allowedWebPageItemIds = (await objectQueryAclPermissionExtender
            .JoinAclPermissions(
                baseQuery,
                user.UserID,
                JoinTypeEnum.Inner,
                new WhereCondition().WhereEquals(
                    nameof(WebPageAclRolePermissionInfo.WebPageAclRolePermissionPermissionName),
                    WebPageAclPermissions.READ),
                nameof(WebPageAclMappingInfo.WebPageAclMappingWebPageAclID))
            .Column(nameof(WebPageAclMappingInfo.WebPageAclMappingWebPageItemID))
            .GetListResultAsync<int>())
            .ToHashSet();

        return [.. webPageItems
            .Where(page => allowedWebPageItemIds.Contains(page.WebPageItemID))
            .Select(page => page.WebPageItemContentItemID)
            .Distinct()];
    }
}

/// <summary>
/// Application name fragments the permission check depends on. They come from the platform's internal
/// registrations rather than a documented contract, so they are pinned here and asserted by a guard test -
/// if an upgrade changes the convention, the test fails instead of the check silently denying everyone.
/// </summary>
internal static class ContentItemGraphPermissionEvaluatorConstants
{
    /// <summary>Value of <see cref="WebsiteConstants.WEBSITE_CHANNEL_APPLICATION_PREFIX" /> in 31.7.3.</summary>
    internal const string WebsiteChannelApplicationPrefix = "Kentico.Xperience.Application.WebPages";

    /// <summary>Value of <see cref="EmailChannelApplication.IDENTIFIER" /> in 31.7.3.</summary>
    internal const string EmailChannelApplicationIdentifier = "Kentico.Xperience.Application.EmailChannel";

    /// <summary>Value of <see cref="HeadlessChannelApplication.IDENTIFIER" /> in 31.7.3.</summary>
    internal const string HeadlessChannelApplicationIdentifier = "Kentico.Xperience.Application.HeadlessChannel";

    /// <summary>Value of <see cref="ContentTypesApplication.IDENTIFIER" /> in 31.7.3.</summary>
    internal const string ContentTypesApplicationIdentifier = "Kentico.Xperience.Application.ContentTypes";

    /// <summary>Value of <see cref="TaxonomyApplication.IDENTIFIER" /> in 31.7.3.</summary>
    internal const string TaxonomyApplicationIdentifier = "Kentico.Xperience.Application.Taxonomy";

    /// <summary>Value of <see cref="FormsApplication.IDENTIFIER" /> in 31.7.3.</summary>
    internal const string FormsApplicationIdentifier = "Kentico.Xperience.Application.Forms";

    /// <summary>Value of <see cref="ModulesApplication.IDENTIFIER" /> in 31.7.3.</summary>
    internal const string ModulesApplicationIdentifier = "Kentico.Xperience.Application.Modules";
}
