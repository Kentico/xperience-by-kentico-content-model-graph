namespace Kentico.Xperience.ContentModelGraph.Tests;

public class ContentItemRelationshipsPageBaseTests
{
    [Test]
    public async Task ValidatePage_WithResolvableItemAndLanguage_IsValid()
    {
        var page = new TestPage(contentItemId: 42, contentLanguageId: 1);

        var result = await page.ValidatePage();

        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public async Task ValidatePage_WithUnresolvableItem_IsInvalidWithObjectNotInitializedKey()
    {
        var page = new TestPage(contentItemId: null, contentLanguageId: 1);

        var result = await page.ValidatePage();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorMessageKey, Is.EqualTo("base.forms.error.objectnotinitialized"));
        });
    }

    [Test]
    public async Task ValidatePage_WithUnresolvableLanguage_IsInvalid()
    {
        var page = new TestPage(contentItemId: 42, contentLanguageId: null);

        var result = await page.ValidatePage();

        Assert.That(result.IsValid, Is.False);
    }

    [Test]
    public async Task ValidatePage_ResolvesEachIdentifierOnlyOnce()
    {
        var page = new TestPage(contentItemId: 42, contentLanguageId: 1);

        await page.ValidatePage();
        await page.ValidatePage();

        Assert.Multiple(() =>
        {
            Assert.That(page.ContentItemIdResolutions, Is.EqualTo(1));
            Assert.That(page.ContentLanguageIdResolutions, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ConfigureTemplateProperties_WithUnresolvableItem_ReturnsPropertiesWithoutGraph()
    {
        var page = new TestPage(contentItemId: null, contentLanguageId: 1);

        var properties = await page.ConfigureTemplateProperties(new TestClientProperties());

        Assert.That(properties.Graph, Is.Null);
    }

    // Dependencies are deliberately null - the paths exercised here never touch them.
    private sealed class TestPage(int? contentItemId, int? contentLanguageId)
        : ContentItemRelationshipsPageBase<TestClientProperties>(null!, null!)
    {
        public int ContentItemIdResolutions { get; private set; }

        public int ContentLanguageIdResolutions { get; private set; }

        protected override Task<int?> ResolveContentItemId()
        {
            ContentItemIdResolutions++;

            return Task.FromResult(contentItemId);
        }

        protected override Task<int?> ResolveContentLanguageId()
        {
            ContentLanguageIdResolutions++;

            return Task.FromResult(contentLanguageId);
        }
    }

    private sealed class TestClientProperties : ContentItemRelationshipsClientPropertiesBase;
}
