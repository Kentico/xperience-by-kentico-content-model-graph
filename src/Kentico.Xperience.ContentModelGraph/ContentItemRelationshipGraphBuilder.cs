using System.Text.Json;

using CMS.ContentEngine;
using CMS.ContentEngine.Internal;
using CMS.DataEngine;
using CMS.EmailLibrary;
using CMS.FormEngine;
using CMS.Headless;
using CMS.Headless.Internal;
using CMS.Websites;
using CMS.Websites.Internal;
using CMS.Workspaces;

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
    ITaxonomyRetriever taxonomyRetriever) : IContentItemRelationshipGraphBuilder
{
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
            throw new InvalidOperationException($"Content item {itemId} was not found.");
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

        ContentItemRelationshipItem MapItem(int relatedItemId)
        {
            var item = items[relatedItemId];
            contentTypes.TryGetValue(item.ContentItemContentTypeID, out var contentType);
            locations.Items.TryGetValue(relatedItemId, out var location);
            var (displayName, displayLanguageId) = GetDisplayName(item, metadata, language.LanguageIds);
            bool isDefaultLanguageFallback = displayLanguageId is int resolvedLanguageId
                && resolvedLanguageId != language.Selected.ContentLanguageID
                && language.Languages.GetValueOrDefault(resolvedLanguageId)?.ContentLanguageIsDefault == true;

            return new ContentItemRelationshipItem
            {
                ItemId = item.ContentItemID,
                Identifier = item.ContentItemGUID.ToString("D"),
                DisplayName = displayName,
                CodeName = item.ContentItemName,
                ContentTypeDisplayName = contentType?.ClassDisplayName ?? string.Empty,
                ContentTypeCodeName = contentType?.ClassName ?? string.Empty,
                ContentTypeAdminUrl = contentType is null ? null : $"/admin/content-types/list/{contentType.ClassID}/fields",
                Kind = ResolveKind(item, contentType),
                LocationName = location?.Name,
                LocationKind = location?.Kind,
                AdminUrl = location?.AdminUrl,
                LiveUrl = location?.LiveUrl,
                IsDefaultLanguageFallback = isDefaultLanguageFallback,
                FallbackLanguageCode = isDefaultLanguageFallback ? language.Languages[displayLanguageId!.Value].ContentLanguageName : null
            };
        }

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
                rootCommonData?.ContentItemCommonDataVisualBuilderWidgets);
            AddRelationships(outgoing, MapItem(reference.ContentItemReferenceTargetItemID), reference.ContentItemReferenceGroupGUID, "outgoing", fields);
        }
        outgoing.AddRange(await GetTaxonomyRelationships(rootItem, fieldsByType, fieldValues, language.Selected.ContentLanguageName));

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
                sourceCommonData.ContentItemCommonDataVisualBuilderWidgets);
            AddRelationships(incoming, MapItem(sourceItemId), reference.ContentItemReferenceGroupGUID, "incoming", fields);
        }

        return new ContentItemRelationshipGraph
        {
            RootItem = MapItem(itemId),
            Incoming = OrderRelationships(incoming),
            Outgoing = OrderRelationships(outgoing)
        };
    }

    private async Task<LanguageContext> GetLanguageContext(int selectedLanguageId)
    {
        var languages = (await languageInfoProvider.Get().GetEnumerableTypedResultAsync())
            .ToDictionary(item => item.ContentLanguageID);
        if (!languages.TryGetValue(selectedLanguageId, out var selected))
        {
            throw new InvalidOperationException($"Content language {selectedLanguageId} was not found.");
        }

        var ids = new List<int>();
        var seen = new HashSet<int>();
        ContentLanguageInfo? current = selected;
        while (current is not null && seen.Add(current.ContentLanguageID))
        {
            ids.Add(current.ContentLanguageID);
            languages.TryGetValue(current.ContentLanguageFallbackContentLanguageID, out current);
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
                nameof(ContentItemCommonDataInfo.ContentItemCommonDataVisualBuilderWidgets))
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
            string languageName = language.Languages.GetValueOrDefault(group.Key.LanguageId)?.ContentLanguageName
                ?? language.Selected.ContentLanguageName;
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
                new ContentQueryExecutionOptions { IncludeSecuredItems = true });

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
        string? widgetsJson)
    {
        if (!items.TryGetValue(targetItemId, out var targetItem))
        {
            return [];
        }

        Guid? pageGuid = webPages.GetValueOrDefault(targetItemId)?.WebPageItemGUID;
        var sources = new List<RelationshipFieldSource>();

        if (fieldsByType.TryGetValue(sourceItem.ContentItemContentTypeID, out var fields)
            && fieldValues.TryGetValue(sourceItem.ContentItemID, out var values))
        {
            sources.AddRange(fields
                .Where(field => values.TryGetValue(field.Name, out string? value)
                    && RelationshipFieldMatcher.MatchesTarget(value, field.DataType, targetItem.ContentItemGUID, pageGuid))
                .Select(field => new RelationshipFieldSource(field.GetDisplayName(null) ?? field.Name, field.Name)));
        }

        foreach (var widget in ReadWidgetReferences(widgetsJson))
        {
            if (widget.Identifier == targetItem.ContentItemGUID || widget.Identifier == pageGuid)
            {
                sources.Add(new RelationshipFieldSource(
                    $"Widget: {widget.TypeIdentifier}",
                    $"widget:{widget.TypeIdentifier}:{widget.PropertyName}"));
            }
        }

        return sources;
    }

    // Page Builder widget properties can reference content items/web pages the same way content type
    // fields do, but those references live in the page's visual builder widget configuration JSON rather
    // than in regular form field values, so they need their own extraction pass.
    private static List<WidgetReference> ReadWidgetReferences(string? widgetsJson)
    {
        var references = new List<WidgetReference>();
        if (string.IsNullOrWhiteSpace(widgetsJson))
        {
            return references;
        }

        try
        {
            using var document = JsonDocument.Parse(widgetsJson);
            if (!TryGetPropertyIgnoreCase(document.RootElement, "EditableAreas", out var areas) || areas.ValueKind != JsonValueKind.Array)
            {
                return references;
            }

            foreach (var area in areas.EnumerateArray())
            {
                if (TryGetPropertyIgnoreCase(area, "Sections", out var sections) && sections.ValueKind == JsonValueKind.Array)
                {
                    CollectWidgetReferences(sections, references);
                }
            }
        }
        catch (JsonException)
        {
            // Ignore malformed configuration; the relationship is still reported, just without a widget label.
        }

        return references;
    }

    private static void CollectWidgetReferences(JsonElement sections, List<WidgetReference> references)
    {
        foreach (var section in sections.EnumerateArray())
        {
            if (!TryGetPropertyIgnoreCase(section, "Zones", out var zones) || zones.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var zone in zones.EnumerateArray())
            {
                if (!TryGetPropertyIgnoreCase(zone, "Widgets", out var widgets) || widgets.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var widget in widgets.EnumerateArray())
                {
                    string typeIdentifier = TryGetPropertyIgnoreCase(widget, "TypeIdentifier", out var typeIdentifierElement)
                        ? typeIdentifierElement.GetString() ?? string.Empty
                        : string.Empty;

                    if (!TryGetPropertyIgnoreCase(widget, "Variants", out var variants) || variants.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var variant in variants.EnumerateArray())
                    {
                        if (!TryGetPropertyIgnoreCase(variant, "Properties", out var properties) || properties.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        foreach (var property in properties.EnumerateObject())
                        {
                            foreach (var identifier in ExtractWidgetPropertyIdentifiers(property.Value).Distinct())
                            {
                                references.Add(new WidgetReference(typeIdentifier, property.Name, identifier));
                            }
                        }
                    }
                }
            }
        }
    }

    // Selector widget properties may hold their value either as a JSON-encoded string (matching the
    // shape used by content type fields) or as a native JSON array (plain GUIDs or identifier objects),
    // depending on the widget property component, so both shapes are supported.
    private static IEnumerable<Guid> ExtractWidgetPropertyIdentifiers(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                string? text = value.GetString();
                foreach (var identifier in RelationshipFieldMatcher.ReadIdentifiers(text, FieldDataType.ContentItemReference)
                    .Concat(RelationshipFieldMatcher.ReadIdentifiers(text, FieldDataType.WebPages)))
                {
                    yield return identifier;
                }
                break;

            case JsonValueKind.Array:
                foreach (var element in value.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.String && element.TryGetGuid(out var plainIdentifier))
                    {
                        yield return plainIdentifier;
                    }
                    else if (element.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var member in element.EnumerateObject())
                        {
                            if ((string.Equals(member.Name, "Identifier", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(member.Name, "WebPageGuid", StringComparison.OrdinalIgnoreCase))
                                && member.Value.TryGetGuid(out var identifier))
                            {
                                yield return identifier;
                            }
                        }
                    }
                }
                break;
        }
    }

    private async Task<IReadOnlyList<ContentItemRelationship>> GetTaxonomyRelationships(
        ContentItemInfo rootItem,
        IReadOnlyDictionary<int, FormFieldInfo[]> fieldsByType,
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, string?>> fieldValues,
        string languageName)
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
                        Kind = "taxonomy"
                    },
                    $"outgoing:taxonomy:{selection.Field.Name}:{tag.Identifier:D}",
                    "outgoing",
                    new RelationshipFieldSource(selection.Field.GetDisplayName(null) ?? selection.Field.Name, selection.Field.Name));
            })
            .ToArray();
    }

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
        var channels = (await channelInfoProvider.Get().GetEnumerableTypedResultAsync()).ToDictionary(channel => channel.ChannelID);
        var workspaces = (await workspaceInfoProvider.Get().GetEnumerableTypedResultAsync()).ToDictionary(workspace => workspace.WorkspaceID);
        var websiteChannels = (await websiteChannelInfoProvider.Get().GetEnumerableTypedResultAsync()).ToDictionary(channel => channel.WebsiteChannelID);
        var headlessChannels = (await headlessChannelInfoProvider.Get().GetEnumerableTypedResultAsync()).ToDictionary(channel => channel.HeadlessChannelID);
        var emailChannels = (await emailChannelInfoProvider.Get().GetEnumerableTypedResultAsync()).ToDictionary(channel => channel.EmailChannelID);
        var urlPaths = await GetUrlPaths([.. pages.Values.Select(page => page.WebPageItemID)], language.LanguageIds);

        var locations = new Dictionary<int, ItemLocation>();
        foreach (var item in items)
        {
            string languageName = language.Languages.GetValueOrDefault(itemLanguages.GetValueOrDefault(item.ContentItemID))?.ContentLanguageName
                ?? language.Selected.ContentLanguageName;

            if (pages.TryGetValue(item.ContentItemID, out var page))
            {
                var websiteChannel = websiteChannels.GetValueOrDefault(page.WebPageItemWebsiteChannelID);
                string? domain = websiteChannel?.WebsiteChannelDomain?.Trim().TrimEnd('/');
                string? path = urlPaths.GetValueOrDefault(page.WebPageItemID)?.TrimStart('/');
                locations[item.ContentItemID] = new ItemLocation(
                    GetChannelName(channels, websiteChannel?.WebsiteChannelChannelID, "Website channel"),
                    "Website",
                    $"/admin/webpages-{page.WebPageItemWebsiteChannelID}/{languageName}_{page.WebPageItemID}/content",
                    string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(path) ? null : $"//{domain}/{path}");
            }
            else if (headlessItems.TryGetValue(item.ContentItemID, out var headlessItem))
            {
                var channel = headlessChannels.GetValueOrDefault(headlessItem.HeadlessItemHeadlessChannelID);
                locations[item.ContentItemID] = new ItemLocation(
                    GetChannelName(channels, channel?.HeadlessChannelChannelID, "Headless channel"),
                    "Headless",
                    $"/admin/headless-{headlessItem.HeadlessItemHeadlessChannelID}/{languageName}/list/{headlessItem.HeadlessItemID}",
                    null);
            }
            else if (emails.TryGetValue(item.ContentItemID, out var email))
            {
                var channel = emailChannels.GetValueOrDefault(email.EmailConfigurationEmailChannelID);
                locations[item.ContentItemID] = new ItemLocation(
                    GetChannelName(channels, channel?.EmailChannelChannelID, "Email channel"),
                    "Email",
                    $"/admin/emails-{email.EmailConfigurationEmailChannelID}/{languageName}/list/{email.EmailConfigurationID}",
                    null);
            }
            else
            {
                locations[item.ContentItemID] = new ItemLocation(
                    workspaces.GetValueOrDefault(item.ContentItemWorkspaceID)?.WorkspaceDisplayName ?? "Content hub",
                    "Content hub",
                    $"/admin/content-hub/{item.ContentItemWorkspaceID}/{languageName}/all/list/{item.ContentItemID}/content",
                    null);
            }
        }

        return new LocationLookups(locations, pages);
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
                nameof(ContentItemCommonDataInfo.ContentItemCommonDataVisualBuilderWidgets))
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

    private static string GetChannelName(IReadOnlyDictionary<int, ChannelInfo> channels, int? channelId, string fallback) =>
        channelId is int id ? channels.GetValueOrDefault(id)?.ChannelDisplayName ?? fallback : fallback;

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

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

    private sealed record LanguageContext(
        ContentLanguageInfo Selected,
        IReadOnlyList<int> LanguageIds,
        IReadOnlyDictionary<int, ContentLanguageInfo> Languages);
    private sealed record FieldValueRow(int ItemId, IReadOnlyDictionary<string, string?> Values);
    private sealed record TaxonomySelection(FormFieldInfo Field, Guid Identifier);
    private sealed record ItemLocation(string Name, string Kind, string AdminUrl, string? LiveUrl);
    private sealed record LocationLookups(
        IReadOnlyDictionary<int, ItemLocation> Items,
        IReadOnlyDictionary<int, WebPageItemInfo> WebPages);
    private sealed record RelationshipFieldSource(string Label, string CodeName);
    private sealed record WidgetReference(string TypeIdentifier, string PropertyName, Guid Identifier);
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
