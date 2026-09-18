using CMS.ContentEngine;

using DancingGoat.Models;

using Kentico.EmailBuilder.Web.Mvc;
using Kentico.Xperience.Admin.Base.FormAnnotations;

namespace DancingGoat.EmailComponents;

/// <summary>
/// Configurable properties of the <see cref="DancingGoatProductWidget"/>.
/// </summary>
public class DancingGoatProductWidgetProperties : IEmailWidgetProperties
{
    /// <summary>
    /// The product page displayed by the widget.
    /// </summary>
    [ContentItemSelectorComponent(
        ProductPage.CONTENT_TYPE_NAME,
        Order = 1,
        Label = "Product page",
        ExplanationText = "The product displayed by the widget.",
        MaximumItems = 1)]
    public IEnumerable<ContentItemReference> ProductPages { get; set; } = [];


    /// <summary>
    /// The text of the call to action linking to the product page.
    /// </summary>
    [TextInputComponent(
        Order = 2,
        Label = "Call to action text",
        ExplanationText = "The text of the button linking to the product page.")]
    public string CallToActionText { get; set; } = "Learn more";
}
