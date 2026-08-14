using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using CMS.Commerce;
using CMS.ContentEngine;

using DancingGoat;
using DancingGoat.Commerce;
using DancingGoat.Helpers;
using DancingGoat.Models;
using DancingGoat.Services;

using Kentico.Commerce.Web.Mvc;
using Kentico.Content.Web.Mvc.Routing;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

[assembly: RegisterWebPageRoute(ShoppingCart.CONTENT_TYPE_NAME, typeof(DancingGoatShoppingCartController), WebsiteChannelNames = new[] { DancingGoatConstants.WEBSITE_CHANNEL_NAME })]

namespace DancingGoat.Commerce;

/// <summary>
/// Controller for managing the shopping cart.
/// </summary>
public sealed class DancingGoatShoppingCartController : Controller
{
    private readonly ICurrentShoppingCartRetriever currentShoppingCartRetriever;
    private readonly ICurrentShoppingCartCreator currentShoppingCartCreator;
    private readonly ProductVariantsExtractor productVariantsExtractor;
    private readonly WebPageUrlProvider webPageUrlProvider;
    private readonly ProductRepository productRepository;
    private readonly PromotionCouponRepository promotionCouponRepository;
    private readonly CalculationService calculationService;
    private readonly UpsellOrderDiscountService upsellOrderDiscountService;
    private readonly IStringLocalizer<SharedResources> localizer;
    private readonly IPriceFormatter priceFormatter;

    public const string UPDATE_ITEM_QUANTITY = "UpdateItemQuantity";
    public const string REMOVE_ITEM = "RemoveItem";

    public const string ADD_COUPON_CODE = "AddCoupon";
    public const string REMOVE_COUPON_CODE = "RemoveCoupon";


    public DancingGoatShoppingCartController(
        ICurrentShoppingCartRetriever currentShoppingCartRetriever,
        ICurrentShoppingCartCreator currentShoppingCartCreator,
        ProductVariantsExtractor productVariantsExtractor,
        WebPageUrlProvider webPageUrlProvider,
        ProductRepository productRepository,
        PromotionCouponRepository promotionCouponRepository,
        CalculationService calculationService,
        UpsellOrderDiscountService upsellOrderDiscountService,
        IStringLocalizer<SharedResources> localizer,
        IPriceFormatter priceFormatter)
    {
        this.currentShoppingCartRetriever = currentShoppingCartRetriever;
        this.currentShoppingCartCreator = currentShoppingCartCreator;
        this.productVariantsExtractor = productVariantsExtractor;
        this.webPageUrlProvider = webPageUrlProvider;
        this.productRepository = productRepository;
        this.promotionCouponRepository = promotionCouponRepository;
        this.calculationService = calculationService;
        this.upsellOrderDiscountService = upsellOrderDiscountService;
        this.localizer = localizer;
        this.priceFormatter = priceFormatter;
    }


    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var shoppingCart = await currentShoppingCartRetriever.Get(cancellationToken);
        if (shoppingCart == null)
        {
            return View(new ShoppingCartViewModel(Enumerable.Empty<ShoppingCartItemViewModel>(), 0, 0, 0, 0, null, Enumerable.Empty<CouponCodeViewModel>()));
        }

        var shoppingCartData = shoppingCart.GetShoppingCartDataModel();

        var products = await productRepository.GetProductsByIds(shoppingCartData.Items.Select(item => item.ProductIdentifier.Identifier), cancellationToken);
        var productPageUrls = await productRepository.GetProductPageUrls(products.Cast<IContentItemFieldsSource>().Select(p => p.SystemFields.ContentItemID), cancellationToken);

        var calculationResult = await calculationService.CalculateShoppingCart(shoppingCartData, cancellationToken);
        var totalWithoutShippingAndTax = PriceCalculationTotalsCalculator.GetTotalWithoutShippingAndTax(calculationResult);
        var subtotal = PriceCalculationTotalsCalculator.GetSubtotal(calculationResult);
        var subtotalAfterLineDiscount = PriceCalculationTotalsCalculator.GetSubtotalAfterLineDiscount(calculationResult);
        var totalDiscount = PriceCalculationTotalsCalculator.GetTotalDiscountAmount(calculationResult);

        var orderDiscountText = await GetOrderDiscountInfoText(calculationResult, subtotalAfterLineDiscount, shoppingCartData, cancellationToken);

        return View(new ShoppingCartViewModel(
            shoppingCartData.Items.Select(item =>
            {
                var product = products.FirstOrDefault(product => (product as IContentItemFieldsSource)?.SystemFields.ContentItemID == item.ProductIdentifier.Identifier);
                var variantValues = product == null ? null : productVariantsExtractor.ExtractVariantsValue(product);
                var calculationItem = calculationResult.Items.FirstOrDefault(i => i.ProductIdentifier.Identifier == item.ProductIdentifier.Identifier && i.ProductIdentifier.VariantIdentifier == item.ProductIdentifier.VariantIdentifier);

                productPageUrls.TryGetValue(item.ProductIdentifier.Identifier, out var pageUrl);

                return product == null
                    ? null
                    : new ShoppingCartItemViewModel(
                        item.ProductIdentifier.Identifier,
                        FormatProductName(product.ProductFieldName, variantValues, item.ProductIdentifier.VariantIdentifier),
                        product.ProductFieldImage.FirstOrDefault()?.ImageFile.Url,
                        pageUrl,
                        item.Quantity,
                        calculationItem.LineSubtotalAfterLineDiscount / calculationItem.Quantity,
                        calculationItem?.LineSubtotalAfterLineDiscount ?? product.ProductFieldPrice,
                        product.ProductFieldPrice * item.Quantity,
                        calculationItem.PromotionData.CatalogPromotionCandidates.FirstOrDefault(c => c.Applied)?.PromotionCandidate as DancingGoatCatalogPromotionCandidate,
                        item.ProductIdentifier.VariantIdentifier);
            })
            .Where(x => x != null)
            .ToList(),
            totalWithoutShippingAndTax,
            subtotal,
            calculationResult.TotalTax,
            totalDiscount,
            orderDiscountText,
            GetCouponsViewModel(shoppingCartData.CouponCodes, calculationResult)));
    }


    [HttpPost]
    [Route("/ShoppingCart/HandleUpdateRemove")]
    public async Task<IActionResult> HandleUpdateRemove(int contentItemId, int quantity, int? variantId, string action, string languageName, CancellationToken cancellationToken)
    {
        if (string.Equals(action, REMOVE_ITEM, StringComparison.OrdinalIgnoreCase))
        {
            quantity = 0;
        }

        var shoppingCart = await GetCurrentShoppingCart(cancellationToken);

        UpdateQuantity(shoppingCart, new ProductVariantIdentifier { Identifier = contentItemId, VariantIdentifier = variantId }, quantity);

        shoppingCart.Update();

        return await RedirectToShoppingCartPage(languageName, cancellationToken);
    }


    [HttpPost]
    [Route("/ShoppingCart/Add")]
    public async Task<IActionResult> Add(int contentItemId, int quantity, int? variantId, string languageName, CancellationToken cancellationToken)
    {
        var shoppingCart = await GetCurrentShoppingCart(cancellationToken);

        UpdateQuantity(shoppingCart, new ProductVariantIdentifier { Identifier = contentItemId, VariantIdentifier = variantId }, quantity);

        shoppingCart.Update();

        return await RedirectToShoppingCartPage(languageName, cancellationToken);
    }


    [HttpPost]
    [Route("/ShoppingCart/HandleCouponCode")]
    public async Task<IActionResult> HandleCouponCode(string couponCode, string action, string languageName, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(couponCode))
        {
            var shoppingCart = await GetCurrentShoppingCart(cancellationToken);
            var shoppingCartData = shoppingCart.GetShoppingCartDataModel();

            // Normalize the promotion code (trim for comparison)
            var normalizedCode = couponCode.Trim();

            if (string.Equals(action, ADD_COUPON_CODE, StringComparison.OrdinalIgnoreCase))
            {
                bool codeAlreadyApplied = shoppingCartData.CouponCodes.Any(c => string.Equals(c, normalizedCode, StringComparison.OrdinalIgnoreCase));
                bool codeExists = await promotionCouponRepository.PromotionCouponExists(normalizedCode, cancellationToken);

                if (!codeAlreadyApplied && codeExists)
                {
                    // Add the new coupon code if it's not already present
                    shoppingCartData.CouponCodes.Add(normalizedCode);
                }
            }
            else if (string.Equals(action, REMOVE_COUPON_CODE, StringComparison.OrdinalIgnoreCase))
            {
                // Remove the coupon code if it's present
                var codeToRemove = shoppingCartData.CouponCodes.FirstOrDefault(c => string.Equals(c, normalizedCode, StringComparison.OrdinalIgnoreCase));
                if (codeToRemove != null)
                {
                    shoppingCartData.CouponCodes.Remove(codeToRemove);
                }
            }

            shoppingCart.StoreShoppingCartDataModel(shoppingCartData);
            shoppingCart.Update();
        }

        return await RedirectToShoppingCartPage(languageName, cancellationToken);
    }


    private static string FormatProductName(string productName, IDictionary<int, string> variants, int? variantId)
    {
        return variants != null && variantId != null && variants.TryGetValue(variantId.Value, out string variantValue)
            ? $"{productName} - {variantValue}"
            : productName;
    }


    /// <summary>
    /// Updates the quantity of the product in the shopping cart.
    /// </summary>
    private static void UpdateQuantity(ShoppingCartInfo shoppingCart, ProductVariantIdentifier productIdentifier, int quantity)
    {
        var shoppingCartData = shoppingCart.GetShoppingCartDataModel();

        var productItem = shoppingCartData.Items.FirstOrDefault(x => x.ProductIdentifier == productIdentifier);
        if (productItem != null)
        {
            productItem.Quantity = quantity;
            if (productItem.Quantity == 0)
            {
                shoppingCartData.Items.Remove(productItem);
            }
        }
        else if (quantity > 0)
        {
            shoppingCartData.Items.Add(new ShoppingCartDataItem
            {
                ProductIdentifier = productIdentifier,
                Quantity = quantity
            });
        }

        shoppingCart.StoreShoppingCartDataModel(shoppingCartData);
    }


    /// <summary>
    /// Gets the current shopping cart or creates a new one if it does not exist.
    /// </summary>
    private async Task<ShoppingCartInfo> GetCurrentShoppingCart(CancellationToken cancellationToken)
    {
        var shoppingCart = await currentShoppingCartRetriever.Get(cancellationToken);

        shoppingCart ??= await currentShoppingCartCreator.Create(cancellationToken);

        return shoppingCart;
    }


    private async Task<RedirectResult> RedirectToShoppingCartPage(string languageName, CancellationToken cancellationToken)
    {
        return Redirect(await webPageUrlProvider.ShoppingCartPageUrl(languageName, cancellationToken));
    }


    private IEnumerable<CouponCodeViewModel> GetCouponsViewModel(IEnumerable<string> couponCodes, DancingGoatPriceCalculationResult calculationResult)
    {
        return couponCodes.Select(code => new CouponCodeViewModel
        {
            Code = code,
            Status = GetCouponStatus(code, calculationResult)
        });


        // Returns the status of the coupon code in the shopping cart context.
        CouponCodeStatus GetCouponStatus(string couponCode, DancingGoatPriceCalculationResult calculationResult)
        {
            foreach (var item in calculationResult.Items)
            {
                var catalogCandidate = item.PromotionData.CatalogPromotionCandidates.FirstOrDefault(
                    c => c.CouponCode?.Equals(couponCode, StringComparison.OrdinalIgnoreCase) ?? false);

                if (catalogCandidate != null)
                {
                    return catalogCandidate.Applied ? CouponCodeStatus.Applied : CouponCodeStatus.Applicable;
                }
            }

            var orderCandidate = calculationResult.PromotionData.OrderPromotionCandidates.FirstOrDefault(
                c => c.CouponCode?.Equals(couponCode, StringComparison.OrdinalIgnoreCase) ?? false);

            if (orderCandidate != null)
            {
                return orderCandidate.Applied ? CouponCodeStatus.Applied : CouponCodeStatus.Applicable;
            }

            return CouponCodeStatus.NotApplicable;
        }
    }


    private async Task<string> GetOrderDiscountInfoText(DancingGoatPriceCalculationResult calculationResult, decimal subtotalAfterLineDiscount, ShoppingCartDataModel shoppingCartData, CancellationToken cancellationToken)
    {
        var appliedOrderPromotion = calculationResult.PromotionData.OrderPromotionCandidates.FirstOrDefault(c => c.Applied);
        var appliedOrderDiscountAmount = appliedOrderPromotion?.PromotionCandidate.OrderDiscountAmount ?? 0;

        var text = await upsellOrderDiscountService.GetUpsellOrderDiscountMessage(subtotalAfterLineDiscount, appliedOrderDiscountAmount, appliedOrderPromotion?.PromotionID, shoppingCartData.CouponCodes, cancellationToken);

        if (string.IsNullOrEmpty(text))
        {
            if (appliedOrderPromotion != null)
            {
                var orderDiscountMessageSource = localizer["You qualified for a {0} discount. Enjoy your discount!"];
                var priceString = priceFormatter.Format(appliedOrderPromotion.PromotionCandidate.OrderDiscountAmount, new PriceFormatContext());
                text = string.Format(orderDiscountMessageSource, priceString);
            }
        }

        return text;
    }
}
