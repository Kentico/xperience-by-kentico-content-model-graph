namespace Kentico.Xperience.ContentModelGraph.Tests;

/// <summary>
/// Covers how a Page Builder reference is labelled on a relationship edge - the source kind discriminator
/// and the readable path shown as the edge tooltip.
/// </summary>
public class PageBuilderEdgeLabelTests
{
    private static PageBuilderReferencePath WidgetPath(string? variantName, bool isPersonalized) =>
        new(PageBuilderSourceKind.Widget, "DancingGoat.LandingPage.HeroImage")
        {
            AreaIdentifier = "top",
            SectionTypeIdentifier = "DancingGoat.SingleColumnSection",
            VariantName = variantName,
            IsPersonalizationVariant = isPersonalized
        };

    [Test]
    public void PageBuilderLabel_NamesTheSourceKind()
    {
        var widget = new PageBuilderReferencePath(PageBuilderSourceKind.Widget, "Kentico.FormWidget");
        var section = new PageBuilderReferencePath(PageBuilderSourceKind.Section, "DancingGoat.SingleColumnSection");
        var template = new PageBuilderReferencePath(PageBuilderSourceKind.Template, "DancingGoat.LandingPageSingleColumn");

        Assert.Multiple(() =>
        {
            Assert.That(ContentItemRelationshipGraphBuilder.PageBuilderLabel([widget]), Is.EqualTo("Widget: Kentico.FormWidget"));
            Assert.That(ContentItemRelationshipGraphBuilder.PageBuilderLabel([section]), Is.EqualTo("Section: DancingGoat.SingleColumnSection"));
            Assert.That(ContentItemRelationshipGraphBuilder.PageBuilderLabel([template]), Is.EqualTo("Template: DancingGoat.LandingPageSingleColumn"));
        });
    }

    [Test]
    public void PageBuilderLabel_NamesTheVariantWhenEveryOccurrenceIsPersonalized()
    {
        string label = ContentItemRelationshipGraphBuilder.PageBuilderLabel([WidgetPath("Sample Requests", true)]);

        Assert.That(label, Is.EqualTo("Widget: DancingGoat.LandingPage.HeroImage · variant \"Sample Requests\""));
    }

    [Test]
    public void PageBuilderLabel_SeveralPersonalizedVariantsCollapseToOneMarker()
    {
        string label = ContentItemRelationshipGraphBuilder.PageBuilderLabel(
            [WidgetPath("Sample Requests", true), WidgetPath("Coffee sale", true)]);

        Assert.That(label, Is.EqualTo("Widget: DancingGoat.LandingPage.HeroImage · personalized"));
    }

    [Test]
    public void PageBuilderLabel_ReferenceAlsoOnTheDefaultVariantIsNotMarked()
    {
        string label = ContentItemRelationshipGraphBuilder.PageBuilderLabel(
            [WidgetPath(null, false), WidgetPath("Sample Requests", true)]);

        Assert.That(label, Is.EqualTo("Widget: DancingGoat.LandingPage.HeroImage"));
    }

    [Test]
    public void PageBuilderPathText_ShowsTheWholeBuilderPath()
    {
        string path = ContentItemRelationshipGraphBuilder.PageBuilderPathText(WidgetPath("Sample Requests", true), "image");

        Assert.That(path, Is.EqualTo(
            "top › DancingGoat.SingleColumnSection › DancingGoat.LandingPage.HeroImage › variant \"Sample Requests\" › image"));
    }

    [Test]
    public void PageBuilderPathText_DefaultVariantHasNoVariantSegment()
    {
        string path = ContentItemRelationshipGraphBuilder.PageBuilderPathText(WidgetPath(null, false), "image");

        Assert.That(path, Is.EqualTo(
            "top › DancingGoat.SingleColumnSection › DancingGoat.LandingPage.HeroImage › image"));
    }

    [Test]
    public void PageBuilderPathText_PersonalizedVariantWithoutNameIsStillMarked()
    {
        string path = ContentItemRelationshipGraphBuilder.PageBuilderPathText(WidgetPath(null, true), "image");

        Assert.That(path, Is.EqualTo(
            "top › DancingGoat.SingleColumnSection › DancingGoat.LandingPage.HeroImage › personalized variant › image"));
    }

    [Test]
    public void PageBuilderPathText_TemplateAndSectionSkipTheLevelsTheyDoNotHave()
    {
        var template = new PageBuilderReferencePath(PageBuilderSourceKind.Template, "DancingGoat.LandingPageSingleColumn");
        var section = new PageBuilderReferencePath(PageBuilderSourceKind.Section, "DancingGoat.SingleColumnSection")
        {
            AreaIdentifier = "top"
        };

        Assert.Multiple(() =>
        {
            Assert.That(
                ContentItemRelationshipGraphBuilder.PageBuilderPathText(template, "images"),
                Is.EqualTo("DancingGoat.LandingPageSingleColumn › images"));
            Assert.That(
                ContentItemRelationshipGraphBuilder.PageBuilderPathText(section, "background"),
                Is.EqualTo("top › DancingGoat.SingleColumnSection › background"));
        });
    }

    [Test]
    public void PageBuilderCodeName_KeepsTheWidgetSchemeAndAddsTheOtherKinds() => Assert.Multiple(() =>
                                                                                       {
                                                                                           Assert.That(
                                                                                               ContentItemRelationshipGraphBuilder.PageBuilderCodeName(WidgetPath("Sample Requests", true), "image"),
                                                                                               // The variant is deliberately absent: it would split one reference into several overlapping edges.
                                                                                               Is.EqualTo("widget:DancingGoat.LandingPage.HeroImage:image"));
                                                                                           Assert.That(
                                                                                               ContentItemRelationshipGraphBuilder.PageBuilderCodeName(
                                                                                                   new PageBuilderReferencePath(PageBuilderSourceKind.Section, "DancingGoat.SingleColumnSection"),
                                                                                                   "background"),
                                                                                               Is.EqualTo("section:DancingGoat.SingleColumnSection:background"));
                                                                                           Assert.That(
                                                                                               ContentItemRelationshipGraphBuilder.PageBuilderCodeName(
                                                                                                   new PageBuilderReferencePath(PageBuilderSourceKind.Template, "DancingGoat.LandingPageSingleColumn"),
                                                                                                   "images"),
                                                                                               Is.EqualTo("template:DancingGoat.LandingPageSingleColumn:images"));
                                                                                       });
}
