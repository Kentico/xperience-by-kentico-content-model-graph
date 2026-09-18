using System.Xml.Linq;

namespace Kentico.Xperience.ContentModelGraph.Tests;

public class ContentModelGraphFieldLabelTests
{
    [Test]
    public void Resolve_ReturnsFieldCaption()
    {
        var field = XElement.Parse("""
            <field column="RelatedArticles">
              <properties>
                <fieldcaption>Related articles</fieldcaption>
              </properties>
            </field>
            """);

        Assert.That(ContentModelGraphFieldLabel.Resolve(field), Is.EqualTo("Related articles"));
    }

    [TestCase("<field column=\"RelatedArticles\" />")]
    [TestCase("<field column=\"RelatedArticles\"><properties><fieldcaption /></properties></field>")]
    public void Resolve_MissingCaptionReturnsFieldCodeName(string xml) => Assert.That(
            ContentModelGraphFieldLabel.Resolve(XElement.Parse(xml)),
            Is.EqualTo("RelatedArticles"));
}
