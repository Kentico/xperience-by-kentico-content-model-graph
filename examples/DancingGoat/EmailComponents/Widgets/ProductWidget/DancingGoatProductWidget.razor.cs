using System;
using System.Linq;
using System.Threading.Tasks;

using CMS.ContentEngine;
using CMS.Websites;

using DancingGoat;
using DancingGoat.EmailComponents;
using DancingGoat.Models;

using Kentico.Content.Web.Mvc;
using Kentico.EmailBuilder.Web.Mvc;

using Microsoft.AspNetCore.Components;

[assembly: RegisterEmailWidget(
    identifier: DancingGoatProductWidget.IDENTIFIER,
    name: "Product",
    componentType: typeof(DancingGoatProductWidget),
    PropertiesType = typeof(DancingGoatProductWidgetProperties),
    IconClass = "icon-box",
    Description = "Displays a product image, description and a link to the product page."
    )]

namespace DancingGoat.EmailComponents;

/// <summary>
/// Product widget component.
/// </summary>
public partial class DancingGoatProductWidget : ComponentBase
{
    /// <summary>
    /// The component identifier.
    /// </summary>
    public const string IDENTIFIER = $"DancingGoat.{nameof(DancingGoatProductWidget)}";


    /// <summary>
    /// The URL of the product image.
    /// </summary>
    private string ImageUrl { get; set; } = string.Empty;


    /// <summary>
    /// The alternative text of the product image.
    /// </summary>
    private string ImageAlternativeText { get; set; } = string.Empty;


    /// <summary>
    /// The product name.
    /// </summary>
    private string ProductName { get; set; } = string.Empty;


    /// <summary>
    /// The product description.
    /// </summary>
    private string ProductDescription { get; set; } = string.Empty;


    /// <summary>
    /// The absolute URL of the product page.
    /// </summary>
    private string ProductPageUrl { get; set; } = string.Empty;


    /// <summary>
    /// The email context accessor used to retrieve the current email context.
    /// </summary>
    [Inject]
    private IEmailContextAccessor EmailContextAccessor { get; set; }


    /// <summary>
    /// The content retriever used to retrieve content items.
    /// </summary>
    [Inject]
    private IContentRetriever ContentRetriever { get; set; }


    /// <summary>
    /// The widget properties.
    /// </summary>
    [Parameter]
    public DancingGoatProductWidgetProperties Properties { get; set; } = null!;


    /// <inheritdoc/>
    protected override async Task OnInitializedAsync()
    {
        await BindProperties();
    }


    private async Task BindProperties()
    {
        var itemGuid = Properties.ProductPages?.Select(i => i.Identifier).FirstOrDefault();

        if (!itemGuid.HasValue || itemGuid.Value == Guid.Empty)
        {
            return;
        }

        var languageName = EmailContextAccessor.GetContext().LanguageName;

        var productPage = (await ContentRetriever.RetrievePages<ProductPage>(
            new RetrievePagesParameters
            {
                ChannelName = DancingGoatConstants.WEBSITE_CHANNEL_NAME,
                LanguageName = languageName,
                LinkedItemsMaxLevel = 2,
                IsForPreview = false,
            },
            query => query
                .Where(where => where.WhereEquals(nameof(IContentQueryDataContainer.ContentItemGUID), itemGuid.Value))
                .TopN(1),
            new RetrievalCacheSettings($"ContentItemGUID_{itemGuid.Value}")))
            .FirstOrDefault();

        var product = productPage?.ProductPageProduct?.FirstOrDefault();

        if (product is null)
        {
            return;
        }

        var image = product.ProductFieldImage?.FirstOrDefault();

        ImageUrl = image?.ImageFile?.Url ?? string.Empty;
        ImageAlternativeText = image?.ImageShortDescription ?? string.Empty;
        ProductName = product.ProductFieldName ?? string.Empty;
        ProductDescription = product.ProductFieldDescription ?? string.Empty;
        ProductPageUrl = productPage.GetUrl().AbsoluteUrl;
    }
}
