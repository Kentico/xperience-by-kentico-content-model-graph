using System.Text.Json;

using CMS.DataEngine;

namespace Kentico.Xperience.ContentModelGraph;

internal static class WidgetReferenceReader
{
    // Page Builder widget properties can reference content items/web pages the same way content type
    // fields do, but those references live in the page's visual builder widget configuration JSON rather
    // than in regular form field values, so they need their own extraction pass.
    //
    // Three separate configuration surfaces can hold a reference, and all three are read here:
    //   - widget properties, per personalization variant (editableAreas -> sections -> zones -> widgets -> variants),
    //   - section properties, which sit on the section itself alongside its zones,
    //   - page template properties, stored in a separate, flat configuration column.
    //
    // Each of the three also carries a "fieldIdentifiers" object mapping every property name to a GUID. That
    // GUID is the ContentItemReferenceGroupGUID Xperience writes on the reference rows the property produces,
    // so it is collected too: it places a reference row on its exact widget variant, section or template
    // property without parsing the value. Email Builder configuration has the same shape and is stored in the
    // same common data columns, so emails go through this reader unchanged.
    internal static PageBuilderReferences ReadPageBuilderReferences(string? widgetsJson, string? templateConfigurationJson)
    {
        var collector = new ReferenceCollector();

        ReadWidgetConfiguration(widgetsJson, collector);
        ReadTemplateConfiguration(templateConfigurationJson, collector);

        return collector.Build();
    }

    private static void ReadWidgetConfiguration(string? widgetsJson, ReferenceCollector collector)
    {
        if (string.IsNullOrWhiteSpace(widgetsJson))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(widgetsJson);
            if (!TryGetPropertyIgnoreCase(document.RootElement, "EditableAreas", out var areas) || areas.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var area in areas.EnumerateArray())
            {
                // Editable area identifiers are authored by the developer ("top", "bottom"), so they are
                // human readable and worth keeping in the reference path.
                string? areaIdentifier = TryGetPropertyIgnoreCase(area, "Identifier", out var areaIdentifierElement)
                    ? areaIdentifierElement.GetString()
                    : null;

                if (TryGetPropertyIgnoreCase(area, "Sections", out var sections) && sections.ValueKind == JsonValueKind.Array)
                {
                    CollectWidgetReferences(sections, areaIdentifier, collector);
                }
            }
        }
        catch (JsonException)
        {
            // Ignore malformed configuration; the relationship is still reported, just without a widget label.
        }
    }

    /// <summary>
    /// Reads the page template configuration, which is flat - a single component identifier with its own
    /// properties, no editable areas, sections, zones, widgets or variants.
    /// </summary>
    private static void ReadTemplateConfiguration(string? templateConfigurationJson, ReferenceCollector collector)
    {
        if (string.IsNullOrWhiteSpace(templateConfigurationJson))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(templateConfigurationJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            // PageTemplateConfiguration serializes its type identifier as "identifier", unlike
            // WidgetConfiguration/SectionConfiguration which use "type".
            string templateIdentifier = TryGetPropertyIgnoreCase(document.RootElement, "Identifier", out var identifierElement)
                ? identifierElement.GetString() ?? string.Empty
                : string.Empty;

            CollectPropertyReferences(
                document.RootElement,
                new PageBuilderReferencePath(PageBuilderSourceKind.Template, templateIdentifier),
                collector);
        }
        catch (JsonException)
        {
            // Ignore malformed configuration, as above.
        }
    }

    internal static void CollectWidgetReferences(JsonElement sections, string? areaIdentifier, ReferenceCollector collector)
    {
        foreach (var section in sections.EnumerateArray())
        {
            // SectionConfiguration.TypeIdentifier is serialized as "type", the same as widgets.
            string sectionType = TryGetPropertyIgnoreCase(section, "type", out var sectionTypeElement)
                ? sectionTypeElement.GetString() ?? string.Empty
                : string.Empty;

            // A section carries its own properties next to its zones, and those properties can hold
            // content item references exactly like widget properties do.
            CollectPropertyReferences(
                section,
                new PageBuilderReferencePath(PageBuilderSourceKind.Section, sectionType) { AreaIdentifier = areaIdentifier },
                collector);

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
                    // WidgetConfiguration.TypeIdentifier is serialized as "type" ([JsonProperty("type")]).
                    string typeIdentifier = TryGetPropertyIgnoreCase(widget, "type", out var typeIdentifierElement)
                        ? typeIdentifierElement.GetString() ?? string.Empty
                        : string.Empty;

                    if (!TryGetPropertyIgnoreCase(widget, "Variants", out var variants) || variants.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var variant in variants.EnumerateArray())
                    {
                        // The default variant carries neither "name" nor "conditionTypeParameters"; a
                        // personalization variant carries at least one of them. A reference that only
                        // exists on a personalization variant is invisible in the builder until that
                        // variant is selected, so the distinction is recorded on the reference path.
                        string? variantName = TryGetPropertyIgnoreCase(variant, "Name", out var variantNameElement)
                            ? variantNameElement.GetString()
                            : null;
                        bool isPersonalized = !string.IsNullOrWhiteSpace(variantName)
                            || (TryGetPropertyIgnoreCase(variant, "ConditionTypeParameters", out var conditionParameters)
                                && conditionParameters.ValueKind == JsonValueKind.Object);

                        CollectPropertyReferences(
                            variant,
                            new PageBuilderReferencePath(PageBuilderSourceKind.Widget, typeIdentifier)
                            {
                                AreaIdentifier = areaIdentifier,
                                SectionTypeIdentifier = sectionType,
                                VariantName = string.IsNullOrWhiteSpace(variantName) ? null : variantName,
                                IsPersonalizationVariant = isPersonalized
                            },
                            collector);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Reads the <c>properties</c> object of a widget variant, a section or a page template. The value shape
    /// is identical across all three, so a single extraction pass covers them.
    /// </summary>
    private static void CollectPropertyReferences(JsonElement owner, PageBuilderReferencePath path, ReferenceCollector collector)
    {
        CollectFieldIdentifiers(owner, path, collector);

        if (!TryGetPropertyIgnoreCase(owner, "Properties", out var properties) || properties.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in properties.EnumerateObject())
        {
            foreach (var identifier in ExtractWidgetPropertyIdentifiers(property.Value).Distinct())
            {
                collector.Add(new WidgetReference(path, property.Name, identifier));
            }

            foreach (string codeName in ExtractWidgetPropertyObjectCodeNames(property.Value).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                collector.Add(new WidgetObjectReference(path, property.Name, codeName));
            }
        }
    }

    /// <summary>
    /// Reads the <c>fieldIdentifiers</c> object of a widget variant, a section or a page template. It maps each
    /// property name to the reference group GUID of the reference rows that property produces. Configuration
    /// saved before the object existed has none, and an entry that is not a GUID is skipped.
    /// </summary>
    private static void CollectFieldIdentifiers(JsonElement owner, PageBuilderReferencePath path, ReferenceCollector collector)
    {
        if (!TryGetPropertyIgnoreCase(owner, "FieldIdentifiers", out var fieldIdentifiers) || fieldIdentifiers.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in fieldIdentifiers.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String
                && property.Value.TryGetGuid(out var referenceGroup)
                && referenceGroup != Guid.Empty)
            {
                collector.Add(new PageBuilderFieldIdentifier(path, property.Name, referenceGroup));
            }
        }
    }

    // Selector widget properties may hold their value either as a JSON-encoded string (matching the
    // shape used by content type fields) or as a native JSON array (plain GUIDs or identifier objects),
    // depending on the widget property component, so both shapes are supported.
    internal static IEnumerable<Guid> ExtractWidgetPropertyIdentifiers(JsonElement value)
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
            case JsonValueKind.Undefined:
                break;
            case JsonValueKind.Object:
                break;
            case JsonValueKind.Number:
                break;
            case JsonValueKind.True:
                break;
            case JsonValueKind.False:
                break;
            case JsonValueKind.Null:
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Reads object references - the shape used by widget properties that select a non-content object,
    /// such as the form of <c>Kentico.FormWidget</c>. Only the code name is read: <c>objectGuid</c> is
    /// null in real configurations, so the code name is the only usable key.
    /// </summary>
    internal static IEnumerable<string> ExtractWidgetPropertyObjectCodeNames(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var element in value.EnumerateArray().Where(element => element.ValueKind == JsonValueKind.Object))
        {
            if (TryGetPropertyIgnoreCase(element, "ObjectCodeName", out var codeName)
                && codeName.ValueKind == JsonValueKind.String)
            {
                string? text = codeName.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    yield return text;
                }
            }
        }
    }

    internal static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            // The explicit loop is deliberate. Replacing it with FirstOrDefault reintroduces a known crash:
            // JsonProperty is a struct, so a miss returns default(JsonProperty) and reading .Name on it throws
            // InvalidOperationException, which 500'd the page for every absent key (covered by regression tests).
            // Rewriting it as Where(...) only trades this rule for S1751, so the loop stays as-is.
#pragma warning disable S3267 // Loops should be simplified using the "Where" LINQ method
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
#pragma warning restore S3267
        }

        value = default;
        return false;
    }

    internal sealed class ReferenceCollector
    {
        private readonly List<WidgetReference> contentReferences = [];
        private readonly List<WidgetObjectReference> objectReferences = [];
        private readonly List<PageBuilderFieldIdentifier> fieldIdentifiers = [];

        internal void Add(WidgetReference reference) => contentReferences.Add(reference);

        internal void Add(WidgetObjectReference reference) => objectReferences.Add(reference);

        internal void Add(PageBuilderFieldIdentifier fieldIdentifier) => fieldIdentifiers.Add(fieldIdentifier);

        internal PageBuilderReferences Build() => new(contentReferences, objectReferences, fieldIdentifiers);
    }
}

/// <summary>
/// Which Page Builder configuration surface a reference was found on. The graph labels its edges
/// differently for each, because a reference from a section or a page template is not a widget reference.
/// </summary>
internal enum PageBuilderSourceKind
{
    Widget,
    Section,
    Template
}

/// <summary>
/// Where in the Page Builder configuration a reference lives. The zone is deliberately omitted - it only
/// carries a GUID, so it adds nothing a person can read.
/// </summary>
/// <param name="SourceKind">Whether the reference came from a widget, a section or the page template.</param>
/// <param name="TypeIdentifier">
/// The widget or section type identifier, or the page template identifier.
/// </param>
internal sealed record PageBuilderReferencePath(PageBuilderSourceKind SourceKind, string TypeIdentifier)
{
    /// <summary>The editable area identifier, which authors define as readable text ("top", "bottom").</summary>
    public string? AreaIdentifier { get; init; }

    /// <summary>The type identifier of the section containing the widget. Unset for section and template references.</summary>
    public string? SectionTypeIdentifier { get; init; }

    /// <summary>The personalization variant display name, when the variant has one.</summary>
    public string? VariantName { get; init; }

    /// <summary>
    /// Set when the reference was found on a personalization variant rather than the default one. Such a
    /// reference is invisible in the builder until an editor selects that variant.
    /// </summary>
    public bool IsPersonalizationVariant { get; init; }
}

/// <summary>A content item or web page reference read from a Page Builder configuration.</summary>
internal sealed record WidgetReference(PageBuilderReferencePath Path, string PropertyName, Guid Identifier)
{
    public PageBuilderSourceKind SourceKind => Path.SourceKind;

    public string TypeIdentifier => Path.TypeIdentifier;
}

/// <summary>An object reference (resolved by code name) read from a Page Builder configuration.</summary>
internal sealed record WidgetObjectReference(PageBuilderReferencePath Path, string PropertyName, string ObjectCodeName)
{
    public PageBuilderSourceKind SourceKind => Path.SourceKind;

    public string TypeIdentifier => Path.TypeIdentifier;
}

/// <summary>
/// One <c>fieldIdentifiers</c> entry: the widget variant, section or page template property whose reference rows
/// carry <paramref name="ReferenceGroup" /> as their <c>ContentItemReferenceGroupGUID</c>.
/// </summary>
internal sealed record PageBuilderFieldIdentifier(PageBuilderReferencePath Path, string PropertyName, Guid ReferenceGroup);

internal sealed record PageBuilderReferences(
    IReadOnlyList<WidgetReference> ContentReferences,
    IReadOnlyList<WidgetObjectReference> ObjectReferences,
    IReadOnlyList<PageBuilderFieldIdentifier> FieldIdentifiers);
