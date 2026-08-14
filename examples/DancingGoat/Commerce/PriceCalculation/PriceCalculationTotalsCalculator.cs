using System.Linq;

namespace DancingGoat.Commerce;

/// <summary>
/// Calculator for getting totals from <see cref="DancingGoatPriceCalculationResult"/>.
/// </summary>
public sealed class PriceCalculationTotalsCalculator
{
    /// <summary>
    /// Gets the subtotal from the calculation result.
    /// </summary>
    /// <param name="calculationResult">The calculation result.</param>
    public static decimal GetSubtotal(DancingGoatPriceCalculationResult calculationResult)
    {
        return calculationResult.Items.Sum(x => x.Quantity * x.ProductData.UnitPrice);
    }


    /// <summary>
    /// Gets the subtotal after line discounts (catalog discounts) were applied.
    /// </summary>
    /// <param name="calculationResult">The calculation result.</param>
    public static decimal GetSubtotalAfterLineDiscount(DancingGoatPriceCalculationResult calculationResult)
    {
        return GetSubtotal(calculationResult) - GetTotalCatalogDiscountAmount(calculationResult);
    }


    /// <summary>
    /// Gets the total discount amount from the calculation result.
    /// </summary>
    /// <param name="calculationResult">The calculation result.</param>
    public static decimal GetTotalDiscountAmount(DancingGoatPriceCalculationResult calculationResult)
    {
        // Calculate the total catalog-level discount across all items (per-item discounts)
        var totalCatalogDiscount = GetTotalCatalogDiscountAmount(calculationResult);

        // Get the order-level discount
        var orderDiscount = GetOrderDiscountAmount(calculationResult);

        return totalCatalogDiscount + orderDiscount;
    }


    /// <summary>
    /// Gets the total without shipping and tax from the calculation result.
    /// </summary>
    /// <param name="calculationResult">The calculation result.</param>
    public static decimal GetTotalWithoutShippingAndTax(DancingGoatPriceCalculationResult calculationResult)
    {
        return calculationResult.Items.Sum(x => x.LineSubtotalAfterAllDiscounts);
    }


    /// <summary>
    /// Gets the order-level discount amount from the calculation result.
    /// </summary>
    /// <param name="calculationResult">The calculation result.</param>
    public static decimal GetOrderDiscountAmount(DancingGoatPriceCalculationResult calculationResult)
    {
        // Get the order-level discount
        var orderDiscount = calculationResult.PromotionData.OrderPromotionCandidates
            .FirstOrDefault(candidate => candidate.Applied)
            ?.PromotionCandidate.OrderDiscountAmount ?? 0;

        return orderDiscount;
    }


    /// <summary>
    /// Gets the sum of all item catalog-level discounts from the calculation result.
    /// </summary>
    /// <param name="calculationResult">The calculation result.</param>
    private static decimal GetTotalCatalogDiscountAmount(DancingGoatPriceCalculationResult calculationResult)
    {
        // Calculate the total catalog-level discount across all items (per-item discounts)
        var totalCatalogDiscount = calculationResult.Items
            .Sum(item =>
            {
                var appliedCandidate = item.PromotionData.CatalogPromotionCandidates
                    .FirstOrDefault(candidate => candidate.Applied);

                var unitPriceDiscount = appliedCandidate?.PromotionCandidate.UnitPriceDiscountAmount ?? 0;

                // Multiply the unit price discount by the item quantity
                return unitPriceDiscount * item.Quantity;
            });

        return totalCatalogDiscount;
    }
}
