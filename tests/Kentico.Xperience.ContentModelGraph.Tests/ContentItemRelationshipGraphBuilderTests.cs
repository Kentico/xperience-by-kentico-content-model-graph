namespace Kentico.Xperience.ContentModelGraph.Tests;

public class ContentItemRelationshipGraphBuilderTests
{
    [Test]
    public void CreateMissingItemGraph_FlagsRootAsMissingWithNoRelationships()
    {
        var graph = ContentItemRelationshipGraphBuilder.CreateMissingItemGraph(42);

        Assert.Multiple(() =>
        {
            Assert.That(graph.RootItem.IsMissing, Is.True);
            Assert.That(graph.RootItem.ItemId, Is.EqualTo(42));
            Assert.That(graph.Incoming, Is.Empty);
            Assert.That(graph.Outgoing, Is.Empty);
        });
    }

    [Test]
    public void CreateMissingItemGraph_DoesNotInventMetadataForTheClient()
    {
        var graph = ContentItemRelationshipGraphBuilder.CreateMissingItemGraph(42);

        Assert.Multiple(() =>
        {
            Assert.That(graph.RootItem.DisplayName, Is.Empty);
            Assert.That(graph.RootItem.CodeName, Is.Empty);
            Assert.That(graph.RootItem.ContentTypeDisplayName, Is.Empty);
            Assert.That(graph.RootItem.ContentTypeCodeName, Is.Empty);
            Assert.That(graph.RootItem.Kind, Is.Empty);
            Assert.That(graph.RootItem.Identifier, Is.Null);
            Assert.That(graph.RootItem.AdminUrl, Is.Null);
            Assert.That(graph.RootItem.LiveUrl, Is.Null);
        });
    }

    [Test]
    public void GetDisclosableDetails_ForAViewableItem_KeepsTheCodeNameAndTheSpecificLocation()
    {
        var (codeName, disclosedLocationName) = ContentItemRelationshipGraphBuilder.GetDisclosableDetails(
            isRestricted: false,
            codeName: "CuppingEvent",
            locationName: "Dancing Goat Commerce",
            locationKind: "Content hub");

        Assert.Multiple(() =>
        {
            Assert.That(codeName, Is.EqualTo("CuppingEvent"));
            Assert.That(disclosedLocationName, Is.EqualTo("Dancing Goat Commerce"));
        });
    }

    // The platform's Content hub "Used in" tab prints the literal "Content hub" in its Channel column for every
    // content hub item - the workspace is named to nobody, so a restricted item's node withholds it too.
    [Test]
    public void GetDisclosableDetails_ForARestrictedItem_ReplacesTheWorkspaceNameWithTheKind()
    {
        var (_, disclosedLocationName) = ContentItemRelationshipGraphBuilder.GetDisclosableDetails(
            isRestricted: true,
            codeName: "CuppingEvent",
            locationName: "Dancing Goat Commerce",
            locationKind: "Content hub");

        Assert.That(disclosedLocationName, Is.EqualTo("Content hub"));
    }

    // The opposite holds for the channel-bound branches. That column is not a redaction, it is just the channel,
    // and the platform shows the real one for restricted items too - observed as "Dancing Goat Emails" and
    // "Dancing Goat Mobile" on items the signed-in reviewer could not read.
    [TestCase("Dancing Goat Pages", "Website")]
    [TestCase("Dancing Goat Mobile", "Headless")]
    [TestCase("Dancing Goat Emails", "Email")]
    public void GetDisclosableDetails_ForARestrictedItem_KeepsTheChannelName(
        string locationName,
        string locationKind)
    {
        var (_, disclosedLocationName) = ContentItemRelationshipGraphBuilder.GetDisclosableDetails(
            isRestricted: true,
            codeName: "CuppingEvent",
            locationName: locationName,
            locationKind: locationKind);

        Assert.Multiple(() =>
        {
            Assert.That(disclosedLocationName, Is.EqualTo(locationName));
            Assert.That(disclosedLocationName, Does.Contain("Dancing Goat"));
            Assert.That(disclosedLocationName, Is.Not.EqualTo(locationKind));
        });
    }

    // The "Used in" list has no code name column at all.
    [Test]
    public void GetDisclosableDetails_ForARestrictedItem_WithholdsTheCodeName()
    {
        var (codeName, _) = ContentItemRelationshipGraphBuilder.GetDisclosableDetails(
            isRestricted: true,
            codeName: "CuppingEvent",
            locationName: "Dancing Goat Commerce",
            locationKind: "Content hub");

        Assert.That(codeName, Is.Empty);
    }

    // An item with no resolved location at all stays without one - restriction must not invent a location.
    [Test]
    public void GetDisclosableDetails_ForARestrictedItemWithNoLocation_EmitsNoLocationName()
    {
        var (_, disclosedLocationName) = ContentItemRelationshipGraphBuilder.GetDisclosableDetails(
            isRestricted: true,
            codeName: "CuppingEvent",
            locationName: null,
            locationKind: null);

        Assert.That(disclosedLocationName, Is.Null);
    }

    // Content types, taxonomy tags and forms live in applications of their own. A reader without access to one
    // of them keeps the label and loses the link - Xperience would block the destination anyway, so offering
    // the door is the bug.
    [Test]
    public void GetApplicationLink_WhenTheApplicationIsAccessible_KeepsTheAdminUrl()
    {
        string? link = ContentItemRelationshipGraphBuilder.GetApplicationLink(
            isApplicationAccessible: true,
            () => "/admin/contenttypes/42/fields");

        Assert.That(link, Is.EqualTo("/admin/contenttypes/42/fields"));
    }

    [Test]
    public void GetApplicationLink_WhenTheApplicationIsNotAccessible_DropsTheAdminUrl()
    {
        string? link = ContentItemRelationshipGraphBuilder.GetApplicationLink(
            isApplicationAccessible: false,
            () => "/admin/contenttypes/42/fields");

        Assert.That(link, Is.Null);
    }

    // Every node in the graph carries a content type link and the graph can hold hundreds of them, so a denied
    // application must not cost a page link generation per node either.
    [Test]
    public void GetApplicationLink_WhenTheApplicationIsNotAccessible_DoesNotBuildTheUrl()
    {
        int builds = 0;

        string? link = ContentItemRelationshipGraphBuilder.GetApplicationLink(
            isApplicationAccessible: false,
            () =>
            {
                builds++;

                return "/admin/forms/7/builder";
            });

        Assert.Multiple(() =>
        {
            Assert.That(link, Is.Null);
            Assert.That(builds, Is.Zero);
        });
    }
}
