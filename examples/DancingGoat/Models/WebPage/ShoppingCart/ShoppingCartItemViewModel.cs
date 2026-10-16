using DancingGoat.Commerce;

namespace DancingGoat.Models;

public record ShoppingCartItemViewModel(int ContentItemId, string Name, string ImageUrl, string DetailUrl, int Quantity, decimal TotalPrice, decimal ListPrice, DancingGoatCatalogPromotionCandidate AppliedPromotion, int? VariantId, string Sku = null)
{
    /// <summary>
    /// Indicates whether a catalog promotion discount is applied to the cart item.
    /// </summary>
    public bool HasDiscount => ListPrice > 0 && ListPrice > TotalPrice && AppliedPromotion is not null;


    /// <summary>
    /// Gets the undiscounted price of a single unit, shown as the per-unit price on a cart line.
    /// Derived from <c>ListPrice</c>, which is the undiscounted price of the whole line
    /// (unit list price multiplied by quantity). A discount is shown on the line total instead,
    /// where <c>ListPrice</c> is the struck-through value.
    /// </summary>
    public decimal UnitListPrice => Quantity > 0 ? ListPrice / Quantity : ListPrice;
}
