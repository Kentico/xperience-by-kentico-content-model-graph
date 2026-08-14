using System.Threading;
using System.Threading.Tasks;

using CMS.Commerce;
using CMS.DataEngine;

namespace DancingGoat.Commerce;

/// <summary>
/// Repository for promotion coupon.
/// </summary>
public class PromotionCouponRepository
{
    private readonly IInfoProvider<PromotionCouponInfo> promotionCouponInfoProvider;


    /// <summary>
    /// Initializes a new instance of the <see cref="PromotionCouponRepository"/> class.
    /// </summary>
    public PromotionCouponRepository(IInfoProvider<PromotionCouponInfo> promotionCouponInfoProvider)
    {
        this.promotionCouponInfoProvider = promotionCouponInfoProvider;
    }


    /// <summary>
    /// Checks if a promotion coupon exists.
    /// </summary>
    /// <param name="couponCode">The coupon code to check.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if the promotion coupon exists, false otherwise.</returns>
    public async Task<bool> PromotionCouponExists(string couponCode, CancellationToken cancellationToken)
    {
        var promotionCouponId = await promotionCouponInfoProvider.Get()
            .WhereEquals(nameof(PromotionCouponInfo.PromotionCouponCode), couponCode)
            .Column(nameof(PromotionCouponInfo.PromotionCouponID))
            .TopN(1)
            .GetScalarResultAsync<int>(cancellationToken: cancellationToken);

        return promotionCouponId > 0;
    }
}
