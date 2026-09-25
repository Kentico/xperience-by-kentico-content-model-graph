using System.Text.Json;

using CMS.ContentEngine;
using CMS.ContentEngine.Internal;
using CMS.DataEngine;
using CMS.EmailLibrary;
using CMS.FormEngine;
using CMS.Headless;
using CMS.Headless.Internal;
using CMS.Helpers;
using CMS.OnlineForms;
using CMS.Websites;
using CMS.Websites.Internal;
using CMS.Workspaces;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.UIPages;

// Aliased rather than imported: only the form administration link needs these page types, and importing the
// namespace wholesale would put every Digital Marketing page type in scope of this file for no benefit.
using DigitalMarketingUIPages = Kentico.Xperience.Admin.DigitalMarketing.UIPages;

namespace Kentico.Xperience.ContentModelGraph;

public sealed class ContentItemRelationshipGraphBuilder(
    IInfoProvider<ContentItemReferenceInfo> referenceInfoProvider,
    IInfoProvider<ContentItemCommonDataInfo> commonDataInfoProvider,
    IInfoProvider<ContentItemInfo> contentItemInfoProvider,
    IInfoProvider<ContentItemLanguageMetadataInfo> languageMetadataInfoProvider,
    IInfoProvider<ContentLanguageInfo> languageInfoProvider,
    IInfoProvider<ChannelInfo> channelInfoProvider,
    IInfoProvider<WorkspaceInfo> workspaceInfoProvider,
    IInfoProvider<WebPageItemInfo> webPageItemInfoProvider,
    IInfoProvider<WebPageUrlPathInfo> webPageUrlPathInfoProvider,
    IInfoProvider<WebsiteChannelInfo> websiteChannelInfoProvider,
    IInfoProvider<HeadlessItemInfo> headlessItemInfoProvider,
    IInfoProvider<HeadlessChannelInfo> headlessChannelInfoProvider,
    IInfoProvider<EmailConfigurationInfo> emailConfigurationInfoProvider,
    IInfoProvider<EmailChannelInfo> emailChannelInfoProvider,
    IContentQueryExecutor contentQueryExecutor,
    IReusableFieldSchemaManager reusableFieldSchemaManager,
    ITaxonomyRetriever taxonomyRetriever,
    IInfoProvider<TagInfo> tagInfoProvider,
    IInfoProvider<BizFormInfo> bizFormInfoProvider,
    IInfoProvider<ContentItemObjectReferenceInfo> objectReferenceInfoProvider,
    IContentItemGraphPermissionEvaluator permissionEvaluator,
    IPageLinkGenerator pageLinkGenerator,
    IProgressiveCache progressiveCache) : IContentItemRelationshipGraphBuilder
{
    /// <summary>
    /// Channels, workspaces and content languages are small reference tables that change only when a
    /// developer or administrator reconfigures the instance, but they were previously read in full on
    /// every graph build - including every <c>ExpandRelationships</c> command, of which a single page
    /// visit can issue dozens. They are cached for an hour and invalidated by the object type dependency
    /// keys below, so a reconfiguration is picked up immediately rather than waiting for the expiration.
    /// </summary>
    private const int REFERENCE_CACHE_MINUTES = 60;
    private const string REFERENCE_CACHE_KEY_PREFIX = "kentico|xperience|contentmodelgraph|relationships";

    /// <summary>
    /// The most content items a form's graph will show. A form embedded on a site-wide footer is reachable
    /// from every page, and each of those items costs a metadata, location and permission evaluation before
    /// it costs a rendered node. The true total is reported through
    /// <see cref="ContentItemRelationshipGraph.Truncations" /> so a capped view never reads as a complete one.
    /// </summary>
    internal const int FORM_USAGE_ITEM_LIMIT = 200;

    public async Task<ContentItemRelationshipGraph> Build(int itemId, int contentLanguageId)
    {
        var language = await GetLanguageContext(contentLanguageId);
        var rootCommonData = await GetRootCommonData(itemId, language.LanguageIds);
        var outgoingReferences = await GetOutgoingReferences(rootCommonData is null ? [] : [rootCommonData]);
        var incomingReferences = (await referenceInfoProvider.Get()
            .Columns(
                nameof(ContentItemReferenceInfo.ContentItemReferenceID),
                nameof(ContentItemReferenceInfo.ContentItemReferenceSourceCommonDataID),
                nameof(ContentItemReferenceInfo.ContentItemReferenceTargetItemID),
                nameof(ContentItemReferenceInfo.ContentItemReferenceGroupGUID))
            .WhereEquals(nameof(ContentItemReferenceInfo.ContentItemReferenceTargetItemID), itemId)
            .GetEnumerableTypedResultAsync()).ToList();

        var incomingCommonData = await GetLatestCommonData(incomingReferences);
        var selectedIncomingReferences = SelectIncomingReferences(incomingReferences, incomingCommonData, contentLanguageId);
        int[] relatedItemIds =
        [
            .. outgoingReferences.Select(reference => reference.ContentItemReferenceTargetItemID)
                .Concat(selectedIncomingReferences.Select(reference => incomingCommonData[reference.ContentItemReferenceSourceCommonDataID].ContentItemCommonDataContentItemID))
                .Append(itemId)
                .Distinct()
        ];

        var items = (await contentItemInfoProvider.Get()
            .Columns(
                nameof(ContentItemInfo.ContentItemID),
                nameof(ContentItemInfo.ContentItemGUID),
                nameof(ContentItemInfo.ContentItemName),
                nameof(ContentItemInfo.ContentItemContentTypeID),
                nameof(ContentItemInfo.ContentItemChannelID),
                nameof(ContentItemInfo.ContentItemWorkspaceID),
                nameof(ContentItemInfo.ContentItemIsReusable))
            .WhereIn(nameof(ContentItemInfo.ContentItemID), relatedItemIds)
            .GetEnumerableTypedResultAsync())
            .ToDictionary(item => item.ContentItemID);

        if (!items.TryGetValue(itemId, out var rootItem))
        {
            // The item was deleted after the graph was rendered. Expansion is a page command, so it
            // cannot fall back to page validation - report the item as missing and stop instead of
            // failing the command or querying references that no longer exist.
            return CreateMissingItemGraph(itemId);
        }

        int[] contentTypeIds = [.. items.Values.Select(item => item.ContentItemContentTypeID).Distinct()];
        var contentTypes = contentTypeIds.Length == 0
            ? []
            : (await DataClassInfoProvider.ProviderObject.Get()
                .Columns(
                    nameof(DataClassInfo.ClassID),
                    nameof(DataClassInfo.ClassName),
                    nameof(DataClassInfo.ClassDisplayName),
                    nameof(DataClassInfo.ClassFormDefinition),
                    nameof(DataClassInfo.ClassContentTypeType))
                .WhereIn(nameof(DataClassInfo.ClassID), contentTypeIds)
                .GetEnumerableTypedResultAsync())
                .ToDictionary(contentType => contentType.ClassID);
        var fieldsByType = contentTypes.Values.ToDictionary(
            contentType => contentType.ClassID,
            contentType => GetRelationshipFields(contentType).ToArray());
        var metadata = await GetMetadata(relatedItemIds, language.LanguageIds);

        var itemLanguages = selectedIncomingReferences
            .GroupBy(reference => incomingCommonData[reference.ContentItemReferenceSourceCommonDataID].ContentItemCommonDataContentItemID)
            .ToDictionary(
                group => group.Key,
                group => incomingCommonData[group.First().ContentItemReferenceSourceCommonDataID].ContentItemCommonDataContentLanguageID);
        itemLanguages[itemId] = rootCommonData?.ContentItemCommonDataContentLanguageID ?? contentLanguageId;
        int[] sourceItemIds = [.. itemLanguages.Keys];
        var fieldValues = await GetFieldValues(
            items.Values.Where(item => sourceItemIds.Contains(item.ContentItemID)),
            fieldsByType,
            itemLanguages,
            language);
        var locations = await GetLocations(items.Values, itemLanguages, language);
        var webPagesByContentItem = locations.WebPages.Values
            .GroupBy(page => page.WebPageItemContentItemID)
            .ToDictionary(group => group.Key, group => group.First());
        // Evaluated once for the whole graph - related items can live in channels or workspaces the current
        // user cannot open, and those nodes must not hand out links into them.
        var viewableItemIds = await permissionEvaluator.GetViewableItemIds([.. items.Keys]);
        // Three application checks for the whole graph, not three per node: the Content types, Taxonomy and
        // Forms links every node offers all point at the same three applications.
        var applications = await permissionEvaluator.GetApplicationAccess();
        var mapping = new ItemMappingContext(items, contentTypes, locations.Items, metadata, language, viewableItemIds, applications);

        ContentItemRelationshipItem MapItem(int relatedItemId) => MapContentItem(mapping, relatedItemId);

        var outgoing = new List<ContentItemRelationship>();
        foreach (var reference in outgoingReferences.Where(reference => items.ContainsKey(reference.ContentItemReferenceTargetItemID)))
        {
            var fields = MatchFields(
                rootItem,
                reference.ContentItemReferenceTargetItemID,
                items,
                webPagesByContentItem,
                fieldsByType,
                fieldValues,
                rootCommonData?.ContentItemCommonDataVisualBuilderWidgets,
                rootCommonData?.ContentItemCommonDataVisualBuilderTemplateConfiguration);
            AddRelationships(outgoing, MapItem(reference.ContentItemReferenceTargetItemID), reference.ContentItemReferenceGroupGUID, "outgoing", fields);
        }
        outgoing.AddRange(await GetTaxonomyRelationships(rootItem, fieldsByType, fieldValues, language.Selected.Name, applications));
        outgoing.AddRange(await GetFormRelationships(rootCommonData, applications));

        var incoming = new List<ContentItemRelationship>();
        foreach (var reference in selectedIncomingReferences)
        {
            var sourceCommonData = incomingCommonData[reference.ContentItemReferenceSourceCommonDataID];
            int sourceItemId = sourceCommonData.ContentItemCommonDataContentItemID;
            if (!items.TryGetValue(sourceItemId, out var sourceItem))
            {
                continue;
            }

            var fields = MatchFields(
                sourceItem,
                itemId,
                items,
                webPagesByContentItem,
                fieldsByType,
                fieldValues,
                sourceCommonData.ContentItemCommonDataVisualBuilderWidgets,
                sourceCommonData.ContentItemCommonDataVisualBuilderTemplateConfiguration);
            AddRelationships(incoming, MapItem(sourceItemId), reference.ContentItemReferenceGroupGUID, "incoming", fields);
        }

        return new ContentItemRelationshipGraph
        {
            RootItem = MapItem(itemId),
            Incoming = OrderRelationships(incoming),
            Outgoing = OrderRelationships(outgoing),
            LanguageCode = language.Selected.Name
        };
    }

    /// <summary>
    /// Builds the graph rooted at a form. The reverse lookup is an indexed foreign key read, not a scan of the
    /// Page Builder JSON: the platform records a <see cref="ContentItemObjectReferenceInfo" /> row for every
    /// form a content item's Page Builder configuration embeds, and that table's only target column is
    /// <c>ContentItemObjectReferenceTargetFormID</c>. The JSON is still read, but only for the items that
    /// survive the cap, and only to label the edges with the widget and property the form sits in.
    /// </summary>
    /// <remarks>
    /// The reference table holds one row per common data <em>version</em>, so the rows are joined to
    /// <see cref="ContentItemCommonDataInfo.ContentItemCommonDataIsLatest" /> - without it superseded versions
    /// and abandoned drafts show up as current usage.
    /// <para>
    /// Not every way a form reaches a page is tracked here: forms rendered directly from view code, forms
    /// referenced by submission notifications, campaigns and automation processes are all invisible to this
    /// table, and a custom component that selects a form by its own means needs <c>[TrackFormReference]</c>
    /// for its usage to be recorded at all.
    /// </para>
    /// </remarks>
    public async Task<ContentItemRelationshipGraph> BuildForForm(int formId)
    {
        var form = (await bizFormInfoProvider.Get()
            .Columns(
                nameof(BizFormInfo.FormID),
                nameof(BizFormInfo.FormGUID),
                nameof(BizFormInfo.FormName),
                nameof(BizFormInfo.FormDisplayName))
            .WhereEquals(nameof(BizFormInfo.FormID), formId)
            .GetEnumerableTypedResultAsync())
            .FirstOrDefault();

        if (form is null)
        {
            return CreateMissingFormGraph();
        }

        // Three application checks for the whole graph, reused by the root node and by every content item node.
        // The root's own link is suppressed like any other: this page is registered under the form editing
        // section, so a reader who got here can open the Forms application - but the graph does not assume it,
        // and no separate gate is added here, as the page's own registration already is one.
        var applications = await permissionEvaluator.GetApplicationAccess();
        var rootItem = CreateFormItem(form, applications);
        int[] referencingCommonDataIds = (await objectReferenceInfoProvider.Get()
            .Columns(nameof(ContentItemObjectReferenceInfo.ContentItemObjectReferenceSourceCommonDataID))
            .WhereEquals(nameof(ContentItemObjectReferenceInfo.ContentItemObjectReferenceTargetFormID), formId)
            .GetEnumerableTypedResultAsync())
            .Select(reference => reference.ContentItemObjectReferenceSourceCommonDataID)
            .Distinct()
            .ToArray();

        if (referencingCommonDataIds.Length == 0)
        {
            return CreateFormGraph(rootItem, [], null);
        }

        var language = await GetDefaultLanguageContext();

        // One row per content item: a form embedded in several language variants of the same page is one node,
        // matching how the client keys nodes (by content item GUID, not by language).
        var latestCommonData = (await commonDataInfoProvider.Get()
            .Columns(
                nameof(ContentItemCommonDataInfo.ContentItemCommonDataID),
                nameof(ContentItemCommonDataInfo.ContentItemCommonDataContentItemID),
                nameof(ContentItemCommonDataInfo.ContentItemCommonDataContentLanguageID))
            .WhereIn(nameof(ContentItemCommonDataInfo.ContentItemCommonDataID), referencingCommonDataIds)
            .WhereTrue(nameof(ContentItemCommonDataInfo.ContentItemCommonDataIsLatest))
            .GetEnumerableTypedResultAsync())
            .GroupBy(data => data.ContentItemCommonDataContentItemID)
            .Select(group => group
                .OrderBy(data => Rank(language.LanguageIds, data.ContentItemCommonDataContentLanguageID))
                .ThenBy(data => data.ContentItemCommonDataContentLanguageID)
                .First())
            .OrderBy(data => data.ContentItemCommonDataContentItemID)
            .ToArray();

        int totalItemCount = latestCommonData.Length;
        var selectedCommonData = latestCommonData.Take(FORM_USAGE_ITEM_LIMIT).ToArray();
        var truncation = totalItemCount > selectedCommonData.Length
            ? new ContentItemRelationshipTruncation
            {
                Direction = "incoming",
                ShownItemCount = selectedCommonData.Length,
                TotalItemCount = totalItemCount
            }
            : null;

        int[] itemIds = [.. selectedCommonData.Select(data => data.ContentItemCommonDataContentItemID)];
        var items = (await contentItemInfoProvider.Get()
            .Columns(
                nameof(ContentItemInfo.ContentItemID),
                nameof(ContentItemInfo.ContentItemGUID),
                nameof(ContentItemInfo.ContentItemName),
                nameof(ContentItemInfo.ContentItemContentTypeID),
                nameof(ContentItemInfo.ContentItemChannelID),
                nameof(ContentItemInfo.ContentItemWorkspaceID),
                nameof(ContentItemInfo.ContentItemIsReusable))
            .WhereIn(nameof(ContentItemInfo.ContentItemID), itemIds)
            .GetEnumerableTypedResultAsync())
            .ToDictionary(item => item.ContentItemID);

        if (items.Count == 0)
        {
            return CreateFormGraph(rootItem, [], truncation, language.Selected.Name);
        }

        int[] contentTypeIds = [.. items.Values.Select(item => item.ContentItemContentTypeID).Distinct()];
        var contentTypes = (await DataClassInfoProvider.ProviderObject.Get()
            .Columns(
                nameof(DataClassInfo.ClassID),
                nameof(DataClassInfo.ClassName),
                nameof(DataClassInfo.ClassDisplayName),
                nameof(DataClassInfo.ClassContentTypeType))
            .WhereIn(nameof(DataClassInfo.ClassID), contentTypeIds)
            .GetEnumerableTypedResultAsync())
            .ToDictionary(contentType => contentType.ClassID);

        var itemLanguages = selectedCommonData.ToDictionary(
            data => data.ContentItemCommonDataContentItemID,
            data => data.ContentItemCommonDataContentLanguageID);
        var metadata = await GetMetadata(itemIds, language.LanguageIds);
        var locations = await GetLocations(items.Values, itemLanguages, language);
        var viewableItemIds = await permissionEvaluator.GetViewableItemIds(itemIds);
        var mapping = new ItemMappingContext(items, contentTypes, locations.Items, metadata, language, viewableItemIds, applications);

        // Read only for the items that survived the cap - these are the two widest columns in the schema.
        var builderConfiguration = (await commonDataInfoProvider.Get()
            .Columns(
                nameof(ContentItemCommonDataInfo.ContentItemCommonDataID),
                nameof(ContentItemCommonDataInfo.ContentItemCommonDataVisualBuilderWidgets),
                nameof(ContentItemCommonDataInfo.ContentItemCommonDataVisualBuilderTemplateConfiguration))
            .WhereIn(
                nameof(ContentItemCommonDataInfo.ContentItemCommonDataID),
                selectedCommonData.Select(data => data.ContentItemCommonDataID))
            .GetEnumerableTypedResultAsync())
            .ToDictionary(data => data.ContentItemCommonDataID);

        var incoming = new List<ContentItemRelationship>();
        foreach (var data in selectedCommonData)
        {
            int itemId = data.ContentItemCommonDataContentItemID;
            if (!items.ContainsKey(itemId))
            {
                continue;
            }

            var item = MapContentItem(mapping, itemId);
            var configuration = builderConfiguration.GetValueOrDefault(data.ContentItemCommonDataID);
            var sources = MatchFormSources(
                configuration?.ContentItemCommonDataVisualBuilderWidgets,
                configuration?.ContentItemCommonDataVisualBuilderTemplateConfiguration,
                form.FormName);

            // The reference row is the authority, the JSON only labels it. A tracked reference the reader
            // cannot place - a custom component registered through [TrackFormReference], say - still gets an
            // unlabelled edge rather than disappearing from the graph.
            if (sources.Count == 0)
            {
                incoming.Add(CreateRelationship(item, $"incoming:{itemId}:form:{form.FormGUID:D}", "incoming", null));
                continue;
            }

            foreach (var source in sources)
            {
                incoming.Add(CreateRelationship(item, $"incoming:{itemId}:form:{form.FormGUID:D}:{source.CodeName}", "incoming", source));
            }
        }

        return CreateFormGraph(rootItem, incoming, truncation, language.Selected.Name);
    }

    /// <summary>
    /// The Page Builder locations one item embeds a given form at, used only to label the edges. An empty
    /// result does not mean the item does not use the form - the reference row is the authority, and a
    /// reference the reader cannot place still produces an unlabelled edge.
    /// </summary>
    internal static IReadOnlyList<RelationshipFieldSource> MatchFormSources(
        string? widgetsJson,
        string? templateConfigurationJson,
        string formName)
    {
        var references = WidgetReferenceReader.ReadPageBuilderReferences(widgetsJson, templateConfigurationJson)
            .ObjectReferences
            .Where(reference => string.Equals(reference.ObjectCodeName, formName, StringComparison.OrdinalIgnoreCase));

        return [.. CreatePageBuilderFieldSources(references.Select(reference => (reference.Path, reference.PropertyName)))];
    }

    internal static ContentItemRelationshipGraph CreateFormGraph(
        ContentItemRelationshipItem rootItem,
        IEnumerable<ContentItemRelationship> incoming,
        ContentItemRelationshipTruncation? truncation,
        string? languageCode = null) => new()
        {
            RootItem = rootItem,
            Incoming = OrderRelationships(incoming),
            Outgoing = [],
            Truncations = truncation is null ? [] : [truncation],
            LanguageCode = languageCode
        };

    /// <summary>
    /// The graph returned for a form that no longer exists, mirroring <see cref="CreateMissingItemGraph" />.
    /// A form node carries no <see cref="ContentItemRelationshipItem.ItemId" />, so the form identifier is not
    /// echoed back and is not taken - the client has nothing to match it against.
    /// </summary>
    internal static ContentItemRelationshipGraph CreateMissingFormGraph() =>
        new()
        {
            RootItem = new ContentItemRelationshipItem
            {
                DisplayName = string.Empty,
                CodeName = string.Empty,
                ContentTypeDisplayName = string.Empty,
                ContentTypeCodeName = string.Empty,
                Kind = GraphNodeKind.FORMS,
                IsMissing = true
            },
            Incoming = [],
            Outgoing = []
        };

    /// <summary>
    /// Projects a form into a graph node. Deliberately carries no <see cref="ContentItemRelationshipItem.ItemId" />:
    /// a form is not a content item, so the node stays terminal (the client only offers expansion for nodes that
    /// have one) and no form identifier ever reaches the content item queries or the permission evaluator.
    /// </summary>
    private ContentItemRelationshipItem CreateFormItem(BizFormInfo form, GraphApplicationAccess applications) =>
        new()
        {
            Identifier = form.FormGUID.ToString("D"),
            DisplayName = form.FormDisplayName,
            CodeName = form.FormName,
            ContentTypeDisplayName = "Form",
            ContentTypeCodeName = "form",
            Kind = GraphNodeKind.FORMS,
            AdminUrl = GetApplicationLink(applications.Forms, () => GetFormAdminUrl(pageLinkGenerator, form.FormID))
        };

    /// <summary>
    /// The language context used when the hosting page has no language of its own, as the form application does
    /// not. The default content language is the closest thing to "what an editor would expect to see".
    /// </summary>
    private async Task<LanguageContext> GetDefaultLanguageContext()
    {
        var languages = await GetLanguages();
        var defaultLanguage = languages.Values.FirstOrDefault(language => language.IsDefault)
            ?? languages.Values.OrderBy(language => language.Id).FirstOrDefault()
            ?? throw new InvalidOperationException("No content language is configured.");

        return await GetLanguageContext(defaultLanguage.Id);
    }

    /// <summary>
    /// Projects one content item into a graph node. Everything the node needs was loaded in batch before
    /// the call, so this is pure mapping - it issues no queries and both graph entry points share it.
    /// </summary>
    private ContentItemRelationshipItem MapContentItem(ItemMappingContext context, int relatedItemId)
    {
        var item = context.Items[relatedItemId];
        bool isRestricted = !context.ViewableItemIds.Contains(relatedItemId);
        context.ContentTypes.TryGetValue(item.ContentItemContentTypeID, out var contentType);
        context.Locations.TryGetValue(relatedItemId, out var location);
        var language = context.Language;
        var (displayName, displayLanguageId) = GetDisplayName(item, context.Metadata, language.LanguageIds);
        bool isDefaultLanguageFallback = displayLanguageId is int resolvedLanguageId
            && resolvedLanguageId != language.Selected.Id
            && language.Languages.GetValueOrDefault(resolvedLanguageId)?.IsDefault == true;
        var (disclosableCodeName, disclosableLocationName) =
            GetDisclosableDetails(isRestricted, item.ContentItemName, location?.Name, location?.Kind);

        return new ContentItemRelationshipItem
        {
            ItemId = item.ContentItemID,
            Identifier = item.ContentItemGUID.ToString("D"),
            DisplayName = displayName,
            CodeName = disclosableCodeName,
            ContentTypeDisplayName = contentType?.ClassDisplayName ?? string.Empty,
            ContentTypeCodeName = contentType?.ClassName ?? string.Empty,
            ContentTypeAdminUrl = contentType is null
                ? null
                : GetApplicationLink(context.Applications.ContentTypes, () => GetContentTypeAdminUrl(contentType.ClassID)),
            Kind = ResolveKind(item, contentType),
            LocationName = disclosableLocationName,
            LocationKind = location?.Kind,
            AdminUrl = isRestricted ? null : location?.AdminUrl,
            LiveUrl = isRestricted ? null : location?.LiveUrl,
            IsRestricted = isRestricted,
            IsDefaultLanguageFallback = isDefaultLanguageFallback,
            // Recorded for every item, not only for fallbacks: an export has to say which language each node
            // was read in, and "the requested one" is only true until a fallback happens.
            LanguageCode = displayLanguageId is int displayedLanguageId
                ? language.Languages.GetValueOrDefault(displayedLanguageId)?.Name
                : null
        };
    }

    /// <summary>
    /// The administration link a node may carry into another application. Content types, taxonomy tags and
    /// forms live in applications of their own, governed by application permissions rather than by the
    /// workspace and channel permissions that decide whether an item may be seen - so a user without access
    /// to one of them gets the label as plain text instead of a link. The URL is not built at all when access
    /// is denied: nothing would consume it, and building one is a page link generation per node.
    /// </summary>
    internal static string? GetApplicationLink(bool isApplicationAccessible, Func<string> getAdminUrl) =>
        isApplicationAccessible ? getAdminUrl() : null;

    /// <summary>
    /// The item details a node is allowed to carry, given whether the current user may read the item. Mirrors
    /// what Xperience's own Content hub "Used in" tab discloses about an item the user cannot read: the display
    /// name and content type are shown, no code name is exposed at all - that tab has no code name column - and
    /// the place the item lives is named as far as that tab names it, which is further than a blanket redaction
    /// would go. See <see cref="GetDisclosableLocationName" />. The node, its edges and its position are
    /// unaffected - only what the node says about the item is.
    /// </summary>
    internal static (string CodeName, string? LocationName) GetDisclosableDetails(
        bool isRestricted,
        string codeName,
        string? locationName,
        string? locationKind) =>
        isRestricted
            ? (string.Empty, GetDisclosableLocationName(locationName, locationKind))
            : (codeName, locationName);

    /// <summary>
    /// The location a restricted item's node may name. The platform does not redact its "Used in" Channel
    /// column by permission - it simply prints the channel, so a restricted website, headless or email item
    /// still shows the real channel name there. Content hub items have no channel, so that column prints the
    /// literal "Content hub" for every one of them, readable or not: the workspace is named to nobody, and is
    /// withheld here too. Content hub is therefore the only branch whose
    /// <see cref="ItemLocation.Name" /> is replaced by its <see cref="ItemLocation.Kind" />.
    /// </summary>
    private static string? GetDisclosableLocationName(string? locationName, string? locationKind) =>
        locationKind == ItemLocationKind.CONTENT_HUB ? locationKind : locationName;

    /// <summary>
    /// Builds the graph returned for a content item that no longer exists. Only <see cref="ContentItemRelationshipItem.IsMissing"/>
    /// and <see cref="ContentItemRelationshipItem.ItemId"/> are consumed by the client, which keeps the metadata it
    /// already holds for the node, so the remaining required members are placeholders.
    /// </summary>
    internal static ContentItemRelationshipGraph CreateMissingItemGraph(int itemId) =>
        new()
        {
            RootItem = new ContentItemRelationshipItem
            {
                ItemId = itemId,
                DisplayName = string.Empty,
                CodeName = string.Empty,
                ContentTypeDisplayName = string.Empty,
                ContentTypeCodeName = string.Empty,
                Kind = string.Empty,
                IsMissing = true
            },
            Incoming = [],
            Outgoing = []
        };

    private async Task<LanguageContext> GetLanguageContext(int selectedLanguageId)
    {
        var languages = await GetLanguages();
        if (!languages.TryGetValue(selectedLanguageId, out var selected))
        {
            throw new InvalidOperationException($"Content language {selectedLanguageId} was not found.");
        }

        var ids = new List<int>();
        var seen = new HashSet<int>();
        var current = selected;
        while (current is not null && seen.Add(current.Id))
        {
            ids.Add(current.Id);
            languages.TryGetValue(current.FallbackLanguageId, out current);
        }

        return new LanguageContext(selected, ids, languages);
    }

    private async Task<ContentItemCommonDataInfo?> GetRootCommonData(int itemId, IReadOnlyList<int> languageIds)
    {
        var rows = await commonDataInfoProvider.Get()
            .Columns(
                nameof(ContentItemCommonDataInfo.ContentItemCommonDataID),
                nameof(ContentItemCommonDataInfo.ContentItemCommonDataContentItemID),
                nameof(ContentItemCommonDataInfo.ContentItemCommonDataContentLanguageID),
                nameof(ContentItemCommonDataInfo.ContentItemCommonDataVisualBuilderWidgets),
                nameof(ContentItemCommonDataInfo.ContentItemCommonDataVisualBuilderTemplateConfiguration))
            .WhereEquals(nameof(ContentItemCommonDataInfo.ContentItemCommonDataContentItemID), itemId)
            .WhereIn(nameof(ContentItemCommonDataInfo.ContentItemCommonDataContentLanguageID), languageIds)
            .WhereTrue(nameof(ContentItemCommonDataInfo.ContentItemCommonDataIsLatest))
            .GetEnumerableTypedResultAsync();

        return rows.OrderBy(row => Rank(languageIds, row.ContentItemCommonDataContentLanguageID)).FirstOrDefault();
    }

    private async Task<Dictionary<int, List<ContentItemLanguageMetadataInfo>>> GetMetadata(
        IReadOnlyCollection<int> itemIds,
        IReadOnlyCollection<int> languageIds)
    {
        if (itemIds.Count == 0 || languageIds.Count == 0)
        {
            return [];
        }

        return (await languageMetadataInfoProvider.Get()
            .Columns(
                nameof(ContentItemLanguageMetadataInfo.ContentItemLanguageMetadataContentItemID),
                nameof(ContentItemLanguageMetadataInfo.ContentItemLanguageMetadataContentLanguageID),
                nameof(ContentItemLanguageMetadataInfo.ContentItemLanguageMetadataDisplayName))
            .WhereIn(nameof(ContentItemLanguageMetadataInfo.ContentItemLanguageMetadataContentItemID), itemIds)
            .WhereIn(nameof(ContentItemLanguageMetadataInfo.ContentItemLanguageMetadataContentLanguageID), languageIds)
            .GetEnumerableTypedResultAsync())
            .GroupBy(item => item.ContentItemLanguageMetadataContentItemID)
            .ToDictionary(group => group.Key, group => group.ToList());
    }

    private IEnumerable<FormFieldInfo> GetRelationshipFields(DataClassInfo contentType)
    {
        foreach (var item in new FormInfo(contentType.ClassFormDefinition).ItemsList)
        {
            if (item is FormFieldInfo field && IsRelationshipField(field))
            {
                yield return field;
            }
            else if (item is FormSchemaInfo reference)
            {
                var schema = reusableFieldSchemaManager.Get(reference.Guid);
                if (schema is not null)
                {
                    foreach (var schemaField in reusableFieldSchemaManager.GetSchemaFields(schema.Name).Where(IsRelationshipField))
                    {
                        yield return schemaField;
                    }
                }
            }
        }
    }

    private async Task<Dictionary<int, IReadOnlyDictionary<string, string?>>> GetFieldValues(
        IEnumerable<ContentItemInfo> sourceItems,
        IReadOnlyDictionary<int, FormFieldInfo[]> fieldsByType,
        IReadOnlyDictionary<int, int> itemLanguages,
        LanguageContext language)
    {
        var values = new Dictionary<int, IReadOnlyDictionary<string, string?>>();
        var groups = sourceItems
            .Where(item => fieldsByType.GetValueOrDefault(item.ContentItemContentTypeID)?.Length > 0)
            .GroupBy(item => new
            {
                item.ContentItemContentTypeID,
                LanguageId = itemLanguages.GetValueOrDefault(item.ContentItemID)
            });

        foreach (var group in groups)
        {
            var contentType = DataClassInfoProvider.GetDataClassInfo(group.Key.ContentItemContentTypeID);
            if (contentType is null)
            {
                continue;
            }

            var fields = fieldsByType[group.Key.ContentItemContentTypeID];
            int[] ids = [.. group.Select(item => item.ContentItemID)];
            string languageName = language.Languages.GetValueOrDefault(group.Key.LanguageId)?.Name
                ?? language.Selected.Name;
            bool useFallbacks = group.Key.LanguageId == 0;
            var query = new ContentItemQueryBuilder()
                .ForContentType(contentType.ClassName, config => config
                    .Where(where => where.WhereIn(nameof(IContentQueryDataContainer.ContentItemID), ids)))
                .InLanguage(languageName, useFallbacks);
            var rows = await contentQueryExecutor.GetResult(
                query,
                container => new FieldValueRow(
                    container.ContentItemID,
                    fields.ToDictionary(
                        field => field.Name,
                        field => (string?)container.GetValue<string>(field.Name),
                        StringComparer.OrdinalIgnoreCase)),
                // ForPreview retrieves the latest version regardless of workflow state, matching the
                // ContentItemCommonDataIsLatest filter used when reading reference rows. Without it the
                // two halves disagree: an edge drawn from a draft reference would find no field value to
                // match, and taxonomy relationships on a never-published item would vanish entirely.
                // Unconditional by design - this is admin UI behind a permission check, and an editor on
                // the relationships tab should see the same version of the content as everywhere else in
                // the administration.
                new ContentQueryExecutionOptions { IncludeSecuredItems = true, ForPreview = true });

            foreach (var row in rows)
            {
                values[row.ItemId] = row.Values;
            }
        }

        return values;
    }

    private static IReadOnlyList<RelationshipFieldSource> MatchFields(
        ContentItemInfo sourceItem,
        int targetItemId,
        IReadOnlyDictionary<int, ContentItemInfo> items,
        IReadOnlyDictionary<int, WebPageItemInfo> webPages,
        IReadOnlyDictionary<int, FormFieldInfo[]> fieldsByType,
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, string?>> fieldValues,
        string? widgetsJson,
        string? templateConfigurationJson)
    {
        if (!items.TryGetValue(targetItemId, out var targetItem))
        {
            return [];
        }

        var pageGuid = webPages.GetValueOrDefault(targetItemId)?.WebPageItemGUID;
        var sources = new List<RelationshipFieldSource>();

        if (fieldsByType.TryGetValue(sourceItem.ContentItemContentTypeID, out var fields)
            && fieldValues.TryGetValue(sourceItem.ContentItemID, out var values))
        {
            sources.AddRange(fields
                .Where(field => values.TryGetValue(field.Name, out string? value)
                    && RelationshipFieldMatcher.MatchesTarget(value, field.DataType, targetItem.ContentItemGUID, pageGuid))
                .Select(field => new RelationshipFieldSource(field.GetDisplayName(null) ?? field.Name, field.Name)));
        }

        var pageBuilderReferences = WidgetReferenceReader.ReadPageBuilderReferences(widgetsJson, templateConfigurationJson);
        sources.AddRange(CreatePageBuilderFieldSources(pageBuilderReferences.ContentReferences
            .Where(reference => reference.Identifier == targetItem.ContentItemGUID || reference.Identifier == pageGuid)
            .Select(reference => (reference.Path, reference.PropertyName))));

        return sources;
    }

    /// <summary>
    /// Turns the Page Builder references pointing at one target into edge sources. References are grouped by
    /// code name, so the same property reused across personalization variants stays a single edge rather than
    /// several edges drawn on top of each other; the variants it was found on are recorded in the path instead.
    /// </summary>
    private static IEnumerable<RelationshipFieldSource> CreatePageBuilderFieldSources(
        IEnumerable<(PageBuilderReferencePath Path, string PropertyName)> references) =>
        references
            .GroupBy(reference => PageBuilderCodeName(reference.Path, reference.PropertyName), StringComparer.Ordinal)
            .Select(group => new RelationshipFieldSource(
                PageBuilderLabel([.. group.Select(reference => reference.Path)]),
                group.Key,
                string.Join(
                    "\n",
                    group
                        .Select(reference => PageBuilderPathText(reference.Path, reference.PropertyName))
                        .Distinct(StringComparer.Ordinal))));

    /// <summary>
    /// The stable identity of a Page Builder reference on an edge. Parallel to the original
    /// <c>widget:{type}:{property}</c> scheme, with the source kind replacing the hardcoded prefix.
    /// </summary>
    internal static string PageBuilderCodeName(PageBuilderReferencePath path, string propertyName) =>
        $"{SourceKindCodeName(path.SourceKind)}:{path.TypeIdentifier}:{propertyName}";

    private static string SourceKindCodeName(PageBuilderSourceKind sourceKind) => sourceKind switch
    {
        PageBuilderSourceKind.Section => "section",
        PageBuilderSourceKind.Template => "template",
        PageBuilderSourceKind.Widget => "widget",
        _ => "widget"
    };

    private static string SourceKindLabel(PageBuilderSourceKind sourceKind) => sourceKind switch
    {
        PageBuilderSourceKind.Section => "Section",
        PageBuilderSourceKind.Template => "Template",
        PageBuilderSourceKind.Widget => "Widget",
        _ => "Widget"
    };

    /// <summary>
    /// The edge label. When every occurrence of the reference sits on a personalization variant the label says
    /// so: such a reference is invisible in the builder until an editor selects that variant, which is exactly
    /// the hidden usage this page exists to surface.
    /// </summary>
    internal static string PageBuilderLabel(IReadOnlyCollection<PageBuilderReferencePath> paths)
    {
        var first = paths.First();
        string label = $"{SourceKindLabel(first.SourceKind)}: {first.TypeIdentifier}";
        if (!paths.All(path => path.IsPersonalizationVariant))
        {
            return label;
        }

        string[] names =
        [
            .. paths
                .Where(path => !string.IsNullOrWhiteSpace(path.VariantName))
                .Select(path => path.VariantName!)
                .Distinct(StringComparer.Ordinal)
        ];

        return names.Length == 1 ? $"{label} · variant \"{names[0]}\"" : $"{label} · personalized";
    }

    /// <summary>
    /// The readable Page Builder path shown on the edge tooltip, for example
    /// <c>top › DancingGoat.SingleColumnSection › DancingGoat.LandingPage.HeroImage › variant "Sample Requests" › image</c>.
    /// </summary>
    internal static string PageBuilderPathText(PageBuilderReferencePath path, string propertyName)
    {
        var segments = new List<string>();
        if (!string.IsNullOrWhiteSpace(path.AreaIdentifier))
        {
            segments.Add(path.AreaIdentifier);
        }

        if (!string.IsNullOrWhiteSpace(path.SectionTypeIdentifier))
        {
            segments.Add(path.SectionTypeIdentifier);
        }

        if (!string.IsNullOrWhiteSpace(path.TypeIdentifier))
        {
            segments.Add(path.TypeIdentifier);
        }

        if (path.IsPersonalizationVariant)
        {
            segments.Add(string.IsNullOrWhiteSpace(path.VariantName)
                ? "personalized variant"
                : $"variant \"{path.VariantName}\"");
        }

        segments.Add(propertyName);

        return string.Join(" › ", segments);
    }

    private async Task<IReadOnlyList<ContentItemRelationship>> GetTaxonomyRelationships(
        ContentItemInfo rootItem,
        IReadOnlyDictionary<int, FormFieldInfo[]> fieldsByType,
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, string?>> fieldValues,
        string languageName,
        GraphApplicationAccess applications)
    {
        if (!fieldsByType.TryGetValue(rootItem.ContentItemContentTypeID, out var fields)
            || !fieldValues.TryGetValue(rootItem.ContentItemID, out var values))
        {
            return [];
        }

        var selections = fields
            .Where(field => string.Equals(field.DataType, FieldDataType.Taxonomy, StringComparison.OrdinalIgnoreCase))
            .SelectMany(field => RelationshipFieldMatcher.ReadIdentifiers(values.GetValueOrDefault(field.Name), field.DataType)
                .Select(identifier => new TaxonomySelection(field, identifier)))
            .DistinctBy(selection => new { selection.Field.Name, selection.Identifier })
            .ToArray();
        Guid[] identifiers = [.. selections.Select(selection => selection.Identifier).Distinct()];
        if (identifiers.Length == 0)
        {
            return [];
        }

        var tags = (await taxonomyRetriever.RetrieveTags(identifiers, languageName))
            .GroupBy(tag => tag.Identifier)
            .ToDictionary(group => group.Key, group => group.First());
        // The Tag model only carries the tag GUID, but the administration URL needs the numeric tag and
        // taxonomy identifiers. One batched query covers every tag referenced by the item.
        var tagAdminUrls = await GetTagAdminUrls([.. tags.Keys], applications.Taxonomy);
        return selections
            .Where(selection => tags.ContainsKey(selection.Identifier))
            .Select(selection =>
            {
                var tag = tags[selection.Identifier];
                return CreateRelationship(
                    new ContentItemRelationshipItem
                    {
                        Identifier = tag.Identifier.ToString("D"),
                        DisplayName = tag.Title,
                        CodeName = tag.Name,
                        ContentTypeDisplayName = "Taxonomy tag",
                        ContentTypeCodeName = "taxonomy",
                        // ItemId intentionally stays unset - the client uses it to expand a node through the
                        // content item relationship command, and a tag is not a content item.
                        Kind = GraphNodeKind.TAXONOMY,
                        AdminUrl = tagAdminUrls.GetValueOrDefault(tag.Identifier)
                    },
                    $"outgoing:taxonomy:{selection.Field.Name}:{tag.Identifier:D}",
                    "outgoing",
                    new RelationshipFieldSource(selection.Field.GetDisplayName(null) ?? selection.Field.Name, selection.Field.Name));
            })
            .ToArray();
    }

    /// <summary>
    /// Page Builder widgets can embed a form. <c>Kentico.FormWidget</c> stores it as an object reference keyed by
    /// code name rather than as a content item reference, so the platform records no reference row for it and the
    /// relationship has to be read out of the builder configuration itself.
    /// </summary>
    /// <remarks>
    /// A form is not a content item, so the node deliberately carries no <see cref="ContentItemRelationshipItem.ItemId"/>.
    /// That keeps it terminal - the client only offers expansion for nodes that have one - and keeps a form identifier
    /// out of the content item queries and out of the permission evaluator's content item path.
    /// </remarks>
    private async Task<IReadOnlyList<ContentItemRelationship>> GetFormRelationships(
        ContentItemCommonDataInfo? rootCommonData,
        GraphApplicationAccess applications)
    {
        if (rootCommonData is null)
        {
            return [];
        }

        var references = WidgetReferenceReader.ReadPageBuilderReferences(
            rootCommonData.ContentItemCommonDataVisualBuilderWidgets,
            rootCommonData.ContentItemCommonDataVisualBuilderTemplateConfiguration).ObjectReferences;
        if (references.Count == 0)
        {
            return [];
        }

        // An object reference carries no object type, only a code name, so the code names are matched against
        // forms in one batched query. A reference to any other object type simply finds no form and is ignored.
        string[] codeNames = [.. references.Select(reference => reference.ObjectCodeName).Distinct(StringComparer.OrdinalIgnoreCase)];
        var forms = (await bizFormInfoProvider.Get()
            .Columns(
                nameof(BizFormInfo.FormID),
                nameof(BizFormInfo.FormGUID),
                nameof(BizFormInfo.FormName),
                nameof(BizFormInfo.FormDisplayName))
            .WhereIn(nameof(BizFormInfo.FormName), codeNames)
            .GetEnumerableTypedResultAsync())
            .GroupBy(form => form.FormName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var relationships = new List<ContentItemRelationship>();
        foreach (var group in references.GroupBy(reference => reference.ObjectCodeName, StringComparer.OrdinalIgnoreCase))
        {
            if (!forms.TryGetValue(group.Key, out var form))
            {
                continue;
            }

            var item = CreateFormItem(form, applications);

            foreach (var source in CreatePageBuilderFieldSources(group.Select(reference => (reference.Path, reference.PropertyName))))
            {
                relationships.Add(CreateRelationship(item, $"outgoing:form:{form.FormGUID:D}:{source.CodeName}", "outgoing", source));
            }
        }

        return relationships;
    }

    /// <summary>
    /// The administration link to a form, which is its builder in the Forms application. Static and shared
    /// with the content model graph rather than copied there: that graph reaches the same page from a form
    /// class instead of a form, but it is the same page, and one definition keeps the two from drifting
    /// apart. Governed by <see cref="GraphApplicationAccess.Forms" /> at every call site.
    /// </summary>
    internal static string GetFormAdminUrl(IPageLinkGenerator pageLinkGenerator, int formId) =>
        AdminUrlHelper.EnsureAdminPrefix(pageLinkGenerator.GetPath<DigitalMarketingUIPages.FormBuilderTab>(
            new PageParameterValues { { typeof(DigitalMarketingUIPages.FormEditSection), formId } }));

    /// <summary>
    /// Resolves administration URLs for taxonomy tags. <see cref="ITaxonomyRetriever"/> returns tags keyed by
    /// GUID only, so the numeric tag and taxonomy identifiers required by the URL are read in a single batched
    /// query instead of one query per tag. A user who cannot open the Taxonomy application gets no URLs and
    /// the query is skipped: the tag nodes still carry their titles, just not links.
    /// </summary>
    private async Task<Dictionary<Guid, string>> GetTagAdminUrls(Guid[] tagIdentifiers, bool isTaxonomyApplicationAccessible)
    {
        if (tagIdentifiers.Length == 0 || !isTaxonomyApplicationAccessible)
        {
            return [];
        }

        var tags = await tagInfoProvider.Get()
            .Columns(
                nameof(TagInfo.TagID),
                nameof(TagInfo.TagGUID),
                nameof(TagInfo.TagTaxonomyID))
            .WhereIn(nameof(TagInfo.TagGUID), tagIdentifiers)
            .GetEnumerableTypedResultAsync();

        return tags
            .GroupBy(tag => tag.TagGUID)
            .ToDictionary(
                group => group.Key,
                group => GetTagAdminUrl(group.First().TagTaxonomyID, group.First().TagID));
    }

    private string GetTagAdminUrl(int taxonomyId, int tagId) =>
        AdminUrlHelper.EnsureAdminPrefix(pageLinkGenerator.GetPath<TagEdit>(
            new PageParameterValues
            {
                { typeof(TaxonomyEditSection), taxonomyId },
                { typeof(TagEditLayout), tagId }
            }));

    private string GetContentTypeAdminUrl(int classId) =>
        AdminUrlHelper.EnsureAdminPrefix(pageLinkGenerator.GetPath<ContentTypeFields>(
            new PageParameterValues { { typeof(ContentTypeEditSection), classId } }));

    private string GetContentHubAdminUrl(int workspaceId, string languageName, int contentItemId) =>
        AdminUrlHelper.EnsureAdminPrefix(pageLinkGenerator.GetPath<ContentItemEdit>(
            new PageParameterValues
            {
                { typeof(ContentHubWorkspace), workspaceId },
                { typeof(ContentHubContentLanguage), languageName },
                { typeof(ContentHubFolder), ContentHubSlugs.ALL_CONTENT_ITEMS },
                { typeof(ContentItemEditSection), contentItemId }
            }));

    private async Task<LocationLookups> GetLocations(
        IEnumerable<ContentItemInfo> sourceItems,
        IReadOnlyDictionary<int, int> itemLanguages,
        LanguageContext language)
    {
        var items = sourceItems.ToArray();
        int[] itemIds = [.. items.Select(item => item.ContentItemID)];
        var pages = await GetByContentItem(webPageItemInfoProvider, nameof(WebPageItemInfo.WebPageItemContentItemID), itemIds, page => page.WebPageItemContentItemID);
        var headlessItems = await GetByContentItem(headlessItemInfoProvider, nameof(HeadlessItemInfo.HeadlessItemContentItemID), itemIds, item => item.HeadlessItemContentItemID);
        var emails = await GetByContentItem(emailConfigurationInfoProvider, nameof(EmailConfigurationInfo.EmailConfigurationContentItemID), itemIds, email => email.EmailConfigurationContentItemID);
        var channels = await GetChannelNames();
        var workspaces = await GetWorkspaceNames();
        var websiteChannels = await GetWebsiteChannels();
        var headlessChannels = await GetHeadlessChannels();
        var emailChannels = await GetEmailChannels();
        var urlPaths = await GetUrlPaths([.. pages.Values.Select(page => page.WebPageItemID)], language.LanguageIds);

        static int? ResolveChannelId(IReadOnlyDictionary<int, int> map, int key) =>
            map.TryGetValue(key, out int channelId) ? channelId : null;

        var locations = new Dictionary<int, ItemLocation>();
        foreach (var item in items)
        {
            string languageName = language.Languages.GetValueOrDefault(itemLanguages.GetValueOrDefault(item.ContentItemID))?.Name
                ?? language.Selected.Name;

            locations[item.ContentItemID] = ResolveLocation(item, languageName);
        }

        return new LocationLookups(locations, pages);

        ItemLocation ResolveLocation(ContentItemInfo sourceItem, string itemLanguageName)
        {
            if (pages.TryGetValue(sourceItem.ContentItemID, out var page))
            {
                var websiteChannel = websiteChannels.GetValueOrDefault(page.WebPageItemWebsiteChannelID);
                string? domain = websiteChannel?.Domain?.Trim().TrimEnd('/');
                string? path = urlPaths.GetValueOrDefault(page.WebPageItemID)?.TrimStart('/');

                return new ItemLocation(
                    GetChannelName(channels, websiteChannel?.ChannelId, "Website channel"),
                    ItemLocationKind.WEBSITE,
                    // Not generated through IPageLinkGenerator: the Pages application is a dynamic
                    // application (WebsiteChannelDynamicApplicationProvider) whose root slug is composed at
                    // runtime as "webpages-{websiteChannelId}". There is no public API to supply that slug to
                    // IPageLinkGenerator.GetPath, so a generated link cannot be produced without guessing.
                    $"/admin/webpages-{page.WebPageItemWebsiteChannelID}/{itemLanguageName}_{page.WebPageItemID}/content",
                    string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(path) ? null : $"//{domain}/{path}");
            }

            if (headlessItems.TryGetValue(sourceItem.ContentItemID, out var headlessItem))
            {
                return new ItemLocation(
                    GetChannelName(channels, ResolveChannelId(headlessChannels, headlessItem.HeadlessItemHeadlessChannelID), "Headless channel"),
                    ItemLocationKind.HEADLESS,
                    // Not generated through IPageLinkGenerator - see the website channel note above.
                    // The headless application root slug is composed at runtime as "headless-{headlessChannelId}"
                    // by HeadlessChannelDynamicApplicationProvider.
                    $"/admin/headless-{headlessItem.HeadlessItemHeadlessChannelID}/{itemLanguageName}/list/{headlessItem.HeadlessItemID}",
                    null);
            }

            if (emails.TryGetValue(sourceItem.ContentItemID, out var email))
            {
                return new ItemLocation(
                    GetChannelName(channels, ResolveChannelId(emailChannels, email.EmailConfigurationEmailChannelID), "Email channel"),
                    ItemLocationKind.EMAIL,
                    // Not generated through IPageLinkGenerator - see the website channel note above.
                    // The emails application root slug is composed at runtime as "emails-{emailChannelId}".
                    $"/admin/emails-{email.EmailConfigurationEmailChannelID}/{itemLanguageName}/list/{email.EmailConfigurationID}",
                    null);
            }

            return new ItemLocation(
                workspaces.GetValueOrDefault(sourceItem.ContentItemWorkspaceID) ?? ItemLocationKind.CONTENT_HUB,
                ItemLocationKind.CONTENT_HUB,
                GetContentHubAdminUrl(sourceItem.ContentItemWorkspaceID, itemLanguageName, sourceItem.ContentItemID),
                null);
        }
    }

    private async Task<Dictionary<int, string>> GetUrlPaths(int[] pageIds, IReadOnlyList<int> languageIds)
    {
        if (pageIds.Length == 0)
        {
            return [];
        }

        return (await webPageUrlPathInfoProvider.Get()
            .WhereIn(nameof(WebPageUrlPathInfo.WebPageUrlPathWebPageItemID), pageIds)
            .WhereIn(nameof(WebPageUrlPathInfo.WebPageUrlPathContentLanguageID), languageIds)
            .WhereFalse(nameof(WebPageUrlPathInfo.WebPageUrlPathIsDraft))
            .WhereTrue(nameof(WebPageUrlPathInfo.WebPageUrlPathIsCanonical))
            .GetEnumerableTypedResultAsync())
            .GroupBy(path => path.WebPageUrlPathWebPageItemID)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(path => Rank(languageIds, path.WebPageUrlPathContentLanguageID))
                    .ThenBy(path => path.WebPageUrlPathID)
                    .First()
                    .WebPageUrlPath);
    }

    private static async Task<Dictionary<int, T>> GetByContentItem<T>(
        IInfoProvider<T> provider,
        string columnName,
        int[] itemIds,
        Func<T, int> key) where T : AbstractInfo<T>, new()
    {
        if (itemIds.Length == 0)
        {
            return [];
        }

        return (await provider.Get().WhereIn(columnName, itemIds).GetEnumerableTypedResultAsync())
            .GroupBy(key)
            .ToDictionary(group => group.Key, group => group.First());
    }

    private async Task<List<ContentItemReferenceInfo>> GetOutgoingReferences(IReadOnlyCollection<ContentItemCommonDataInfo> rootCommonData)
    {
        if (rootCommonData.Count == 0)
        {
            return [];
        }

        var references = await referenceInfoProvider.Get()
            .Columns(
                nameof(ContentItemReferenceInfo.ContentItemReferenceID),
                nameof(ContentItemReferenceInfo.ContentItemReferenceSourceCommonDataID),
                nameof(ContentItemReferenceInfo.ContentItemReferenceTargetItemID),
                nameof(ContentItemReferenceInfo.ContentItemReferenceGroupGUID))
            .WhereIn(nameof(ContentItemReferenceInfo.ContentItemReferenceSourceCommonDataID), rootCommonData.Select(data => data.ContentItemCommonDataID))
            .GetEnumerableTypedResultAsync();
        return references
            .GroupBy(reference => new { reference.ContentItemReferenceTargetItemID, reference.ContentItemReferenceGroupGUID })
            .Select(group => group.OrderBy(reference => reference.ContentItemReferenceID).First())
            .ToList();
    }

    private async Task<Dictionary<int, ContentItemCommonDataInfo>> GetLatestCommonData(IReadOnlyCollection<ContentItemReferenceInfo> incomingReferences)
    {
        if (incomingReferences.Count == 0)
        {
            return [];
        }

        var commonData = await commonDataInfoProvider.Get()
            .Columns(
                nameof(ContentItemCommonDataInfo.ContentItemCommonDataID),
                nameof(ContentItemCommonDataInfo.ContentItemCommonDataContentItemID),
                nameof(ContentItemCommonDataInfo.ContentItemCommonDataContentLanguageID),
                nameof(ContentItemCommonDataInfo.ContentItemCommonDataVisualBuilderWidgets),
                nameof(ContentItemCommonDataInfo.ContentItemCommonDataVisualBuilderTemplateConfiguration))
            .WhereIn(nameof(ContentItemCommonDataInfo.ContentItemCommonDataID), incomingReferences.Select(reference => reference.ContentItemReferenceSourceCommonDataID))
            .WhereTrue(nameof(ContentItemCommonDataInfo.ContentItemCommonDataIsLatest))
            .GetEnumerableTypedResultAsync();
        return commonData.ToDictionary(data => data.ContentItemCommonDataID);
    }

    private static List<ContentItemReferenceInfo> SelectIncomingReferences(
        IEnumerable<ContentItemReferenceInfo> references,
        IReadOnlyDictionary<int, ContentItemCommonDataInfo> commonData,
        int contentLanguageId) =>
        references
            .Where(reference => commonData.ContainsKey(reference.ContentItemReferenceSourceCommonDataID))
            .GroupBy(reference => new
            {
                SourceItemId = commonData[reference.ContentItemReferenceSourceCommonDataID].ContentItemCommonDataContentItemID,
                reference.ContentItemReferenceTargetItemID,
                reference.ContentItemReferenceGroupGUID
            })
            .Select(group => group
                .OrderByDescending(reference => commonData[reference.ContentItemReferenceSourceCommonDataID].ContentItemCommonDataContentLanguageID == contentLanguageId)
                .ThenBy(reference => commonData[reference.ContentItemReferenceSourceCommonDataID].ContentItemCommonDataContentLanguageID)
                .ThenBy(reference => reference.ContentItemReferenceID)
                .First())
            .ToList();

    private static void AddRelationships(
        ICollection<ContentItemRelationship> relationships,
        ContentItemRelationshipItem item,
        Guid referenceGroupGuid,
        string direction,
        IReadOnlyList<RelationshipFieldSource> sources)
    {
        if (sources.Count == 0)
        {
            relationships.Add(CreateRelationship(item, $"{direction}:{item.ItemId}:{referenceGroupGuid:D}", direction, null));
            return;
        }

        foreach (var source in sources)
        {
            relationships.Add(CreateRelationship(item, $"{direction}:{item.ItemId}:{referenceGroupGuid:D}:{source.CodeName}", direction, source));
        }
    }

    private static ContentItemRelationship CreateRelationship(
        ContentItemRelationshipItem item,
        string id,
        string direction,
        RelationshipFieldSource? source) => new()
        {
            Id = id,
            RelatedItem = item,
            FieldLabel = source?.Label ?? string.Empty,
            FieldCodeName = source?.CodeName ?? string.Empty,
            FieldPath = source?.Path,
            Direction = direction
        };

    private static IReadOnlyList<ContentItemRelationship> OrderRelationships(IEnumerable<ContentItemRelationship> relationships) =>
        relationships
            .OrderBy(relationship => relationship.FieldLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(relationship => relationship.RelatedItem.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(relationship => relationship.Id, StringComparer.Ordinal)
            .ToArray();

    private static (string DisplayName, int? LanguageId) GetDisplayName(
        ContentItemInfo item,
        IReadOnlyDictionary<int, List<ContentItemLanguageMetadataInfo>> metadata,
        IReadOnlyList<int> languageIds)
    {
        var best = metadata.GetValueOrDefault(item.ContentItemID)?
            .OrderBy(value => Rank(languageIds, value.ContentItemLanguageMetadataContentLanguageID))
            .FirstOrDefault();

        return best is null
            ? (item.ContentItemName, null)
            : (best.ContentItemLanguageMetadataDisplayName, best.ContentItemLanguageMetadataContentLanguageID);
    }

    private static string GetChannelName(IReadOnlyDictionary<int, string> channels, int? channelId, string fallback) =>
        channelId is int id ? channels.GetValueOrDefault(id) ?? fallback : fallback;

    private static bool IsRelationshipField(FormFieldInfo field) =>
        string.Equals(field.DataType, FieldDataType.ContentItemReference, StringComparison.OrdinalIgnoreCase)
        || string.Equals(field.DataType, FieldDataType.WebPages, StringComparison.OrdinalIgnoreCase)
        || string.Equals(field.DataType, FieldDataType.Taxonomy, StringComparison.OrdinalIgnoreCase);

    private static int Rank(IReadOnlyList<int> ids, int id)
    {
        for (int index = 0; index < ids.Count; index++)
        {
            if (ids[index] == id)
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    private static string ResolveKind(ContentItemInfo item, DataClassInfo? contentType) =>
        contentType?.ClassContentTypeType?.ToLowerInvariant() switch
        {
            "website" => GraphNodeKind.WEBSITE,
            "reusable" => GraphNodeKind.REUSABLE,
            "email" => GraphNodeKind.EMAIL,
            "headless" => GraphNodeKind.HEADLESS,
            _ when item.ContentItemIsReusable => GraphNodeKind.REUSABLE,
            _ => GraphNodeKind.OBJECT_TYPE
        };

    /// <summary>
    /// Content languages keyed by identifier. Read in full because the fallback chain of the selected
    /// language can reach any row, so the identifiers needed are not known before the lookup runs.
    /// </summary>
    private Task<IReadOnlyDictionary<int, LanguageRef>> GetLanguages() =>
        LoadReference(
            "languages",
            ContentLanguageInfo.OBJECT_TYPE,
            async () => (await languageInfoProvider.Get()
                .Columns(
                    nameof(ContentLanguageInfo.ContentLanguageID),
                    nameof(ContentLanguageInfo.ContentLanguageName),
                    nameof(ContentLanguageInfo.ContentLanguageIsDefault),
                    nameof(ContentLanguageInfo.ContentLanguageFallbackContentLanguageID))
                .GetEnumerableTypedResultAsync())
                .ToDictionary(
                    language => language.ContentLanguageID,
                    language => new LanguageRef(
                        language.ContentLanguageID,
                        language.ContentLanguageName,
                        language.ContentLanguageIsDefault,
                        language.ContentLanguageFallbackContentLanguageID)));

    /// <summary>Channel display names keyed by channel identifier.</summary>
    private Task<IReadOnlyDictionary<int, string>> GetChannelNames() =>
        LoadReference(
            "channels",
            ChannelInfo.OBJECT_TYPE,
            async () => (await channelInfoProvider.Get()
                .Columns(
                    nameof(ChannelInfo.ChannelID),
                    nameof(ChannelInfo.ChannelDisplayName))
                .GetEnumerableTypedResultAsync())
                .ToDictionary(channel => channel.ChannelID, channel => channel.ChannelDisplayName));

    /// <summary>Workspace display names keyed by workspace identifier.</summary>
    private Task<IReadOnlyDictionary<int, string>> GetWorkspaceNames() =>
        LoadReference(
            "workspaces",
            WorkspaceInfo.OBJECT_TYPE,
            async () => (await workspaceInfoProvider.Get()
                .Columns(
                    nameof(WorkspaceInfo.WorkspaceID),
                    nameof(WorkspaceInfo.WorkspaceDisplayName))
                .GetEnumerableTypedResultAsync())
                .ToDictionary(workspace => workspace.WorkspaceID, workspace => workspace.WorkspaceDisplayName));

    /// <summary>Website channel identifiers mapped to the owning channel and the channel domain.</summary>
    private Task<IReadOnlyDictionary<int, WebsiteChannelRef>> GetWebsiteChannels() =>
        LoadReference(
            "websitechannels",
            WebsiteChannelInfo.OBJECT_TYPE,
            async () => (await websiteChannelInfoProvider.Get()
                .Columns(
                    nameof(WebsiteChannelInfo.WebsiteChannelID),
                    nameof(WebsiteChannelInfo.WebsiteChannelChannelID),
                    nameof(WebsiteChannelInfo.WebsiteChannelDomain))
                .GetEnumerableTypedResultAsync())
                .ToDictionary(
                    channel => channel.WebsiteChannelID,
                    channel => new WebsiteChannelRef(channel.WebsiteChannelChannelID, channel.WebsiteChannelDomain)));

    /// <summary>Headless channel identifiers mapped to the owning channel identifier.</summary>
    private Task<IReadOnlyDictionary<int, int>> GetHeadlessChannels() =>
        LoadReference(
            "headlesschannels",
            HeadlessChannelInfo.OBJECT_TYPE,
            async () => (await headlessChannelInfoProvider.Get()
                .Columns(
                    nameof(HeadlessChannelInfo.HeadlessChannelID),
                    nameof(HeadlessChannelInfo.HeadlessChannelChannelID))
                .GetEnumerableTypedResultAsync())
                .ToDictionary(channel => channel.HeadlessChannelID, channel => channel.HeadlessChannelChannelID));

    /// <summary>Email channel identifiers mapped to the owning channel identifier.</summary>
    private Task<IReadOnlyDictionary<int, int>> GetEmailChannels() =>
        LoadReference(
            "emailchannels",
            EmailChannelInfo.OBJECT_TYPE,
            async () => (await emailChannelInfoProvider.Get()
                .Columns(
                    nameof(EmailChannelInfo.EmailChannelID),
                    nameof(EmailChannelInfo.EmailChannelChannelID))
                .GetEnumerableTypedResultAsync())
                .ToDictionary(channel => channel.EmailChannelID, channel => channel.EmailChannelChannelID));

    /// <summary>
    /// Caches a reference table lookup under the object type's <c>|all</c> dependency key, so creating,
    /// editing or deleting a row of that type drops the entry immediately. Whole tables are cached rather
    /// than the identifiers a single request needs: the tables hold a handful of rows each, and keying the
    /// entries by request would fragment the cache and defeat the purpose of caching them at all.
    /// </summary>
    private async Task<IReadOnlyDictionary<int, TValue>> LoadReference<TValue>(
        string name,
        string objectType,
        Func<Task<Dictionary<int, TValue>>> load) =>
        await progressiveCache.LoadAsync(
            _ => load(),
            new CacheSettings(REFERENCE_CACHE_MINUTES, $"{REFERENCE_CACHE_KEY_PREFIX}|{name}")
            {
                GetCacheDependency = () => CacheHelper.GetCacheDependency([$"{objectType}|all"])
            });

    private sealed record LanguageContext(
        LanguageRef Selected,
        IReadOnlyList<int> LanguageIds,
        IReadOnlyDictionary<int, LanguageRef> Languages);
    /// <summary>
    /// The subset of <see cref="ContentLanguageInfo"/> the graph consumes. Reference lookups are cached
    /// across requests, so they project into immutable records rather than handing out shared, partially
    /// loaded <see cref="AbstractInfo{TInfo}"/> instances.
    /// </summary>
    private sealed record LanguageRef(int Id, string Name, bool IsDefault, int FallbackLanguageId);
    private sealed record WebsiteChannelRef(int ChannelId, string? Domain);
    private sealed record FieldValueRow(int ItemId, IReadOnlyDictionary<string, string?> Values);
    private sealed record TaxonomySelection(FormFieldInfo Field, Guid Identifier);
    /// <summary>
    /// Where an item lives. <paramref name="Name" /> is the specific place - the workspace or the channel - and
    /// <paramref name="Kind" /> is the generic one, one of the <see cref="ItemLocationKind" /> values. A node for
    /// an item the current user cannot read keeps the channel name but not the workspace; see
    /// <see cref="GetDisclosableLocationName" />.
    /// </summary>
    private sealed record ItemLocation(string Name, string Kind, string AdminUrl, string? LiveUrl);
    private sealed record LocationLookups(
        IReadOnlyDictionary<int, ItemLocation> Items,
        IReadOnlyDictionary<int, WebPageItemInfo> WebPages);
    /// <summary>Everything <see cref="MapContentItem"/> needs, loaded in batch by the caller.</summary>
    private sealed record ItemMappingContext(
        IReadOnlyDictionary<int, ContentItemInfo> Items,
        IReadOnlyDictionary<int, DataClassInfo> ContentTypes,
        IReadOnlyDictionary<int, ItemLocation> Locations,
        IReadOnlyDictionary<int, List<ContentItemLanguageMetadataInfo>> Metadata,
        LanguageContext Language,
        IReadOnlySet<int> ViewableItemIds,
        GraphApplicationAccess Applications);
    /// <param name="Label">The edge label shown to the user.</param>
    /// <param name="CodeName">The stable identity of the source, used to build the relationship identifier.</param>
    /// <param name="Path">
    /// The readable Page Builder path a reference was found at, one line per occurrence. Null for references
    /// that come from a content type field, which have no path beyond the field itself.
    /// </param>
    internal sealed record RelationshipFieldSource(string Label, string CodeName, string? Path = null);
}

internal static class RelationshipFieldMatcher
{
    internal static bool MatchesTarget(
        string? value,
        string dataType,
        Guid contentItemGuid,
        Guid? webPageGuid) =>
        ReadIdentifiers(value, dataType)
            .Any(identifier => identifier == contentItemGuid || identifier == webPageGuid);

    internal static IReadOnlyList<Guid> ReadIdentifiers(string? value, string dataType)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        string property = string.Equals(dataType, FieldDataType.WebPages, StringComparison.OrdinalIgnoreCase)
            ? "WebPageGuid"
            : "Identifier";
        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var identifiers = new List<Guid>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var member in element.EnumerateObject())
                {
                    if (string.Equals(member.Name, property, StringComparison.OrdinalIgnoreCase)
                        && member.Value.TryGetGuid(out var identifier))
                    {
                        identifiers.Add(identifier);
                    }
                }
            }

            return identifiers;
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
