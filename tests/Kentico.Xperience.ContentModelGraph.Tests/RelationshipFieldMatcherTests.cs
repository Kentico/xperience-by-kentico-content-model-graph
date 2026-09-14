using CMS.DataEngine;

namespace Kentico.Xperience.ContentModelGraph.Tests;

public class RelationshipFieldMatcherTests
{
    private static readonly Guid identifier = Guid.Parse("24bf2c50-06ab-4a58-af52-b7942aa9059b");

    [TestCase("Identifier")]
    [TestCase("identifier")]
    [TestCase("IDENTIFIER")]
    public void ReadIdentifiers_ContentItemPropertyIsCaseInsensitive(string propertyName)
    {
        string json = $$"""[{ "{{propertyName}}": "{{identifier}}" }]""";

        var result = RelationshipFieldMatcher.ReadIdentifiers(json, FieldDataType.ContentItemReference);

        Assert.That(result, Is.EqualTo(new[] { identifier }));
    }

    [Test]
    public void ReadIdentifiers_WebPagesUsesWebPageGuid()
    {
        string json = $$"""[{ "webpageguid": "{{identifier}}", "Identifier": "{{Guid.NewGuid()}}" }]""";

        var result = RelationshipFieldMatcher.ReadIdentifiers(json, FieldDataType.WebPages);

        Assert.That(result, Is.EqualTo(new[] { identifier }));
    }

    [Test]
    public void MatchesTarget_MatchesContentItemOrWebPageIdentifier()
    {
        var webPageIdentifier = Guid.NewGuid();

        Assert.Multiple(() =>
        {
            Assert.That(RelationshipFieldMatcher.MatchesTarget(
                $$"""[{ "Identifier": "{{identifier}}" }]""",
                FieldDataType.ContentItemReference,
                identifier,
                webPageIdentifier), Is.True);
            Assert.That(RelationshipFieldMatcher.MatchesTarget(
                $$"""[{ "WebPageGuid": "{{webPageIdentifier}}" }]""",
                FieldDataType.WebPages,
                identifier,
                webPageIdentifier), Is.True);
            Assert.That(RelationshipFieldMatcher.MatchesTarget(
                $$"""[{ "Identifier": "{{Guid.NewGuid()}}" }]""",
                FieldDataType.ContentItemReference,
                identifier,
                webPageIdentifier), Is.False);
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("not json")]
    [TestCase("{}")]
    [TestCase("[1, null, {}]")]
    public void ReadIdentifiers_InvalidOrUnrelatedValueReturnsEmpty(string? value)
    {
        var result = RelationshipFieldMatcher.ReadIdentifiers(value, FieldDataType.Taxonomy);

        Assert.That(result, Is.Empty);
    }
}
