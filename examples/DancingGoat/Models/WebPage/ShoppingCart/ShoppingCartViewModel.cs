using System.Collections.Generic;

using DancingGoat.Commerce;

namespace DancingGoat.Models;

public record ShoppingCartViewModel(IEnumerable<ShoppingCartItemViewModel> Items, decimal TotalPrice, decimal SubtotalPrice, decimal TotalTax, decimal TotalDiscount, string OrderDiscountText, IEnumerable<CouponCodeViewModel> CouponCodes);
