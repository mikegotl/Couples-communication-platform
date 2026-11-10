using System.Security.Cryptography;
using Couples.Application.Abstractions;
using Couples.Application.Rewards;
using Couples.Domain.Common;
using Couples.Domain.Rewards;
using Couples.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Couples.Infrastructure.Rewards;

/// <summary>
/// The built-in provider: offers come from the admin-managed <c>reward_offers</c> table and
/// vouchers are codes this app issues. A participating business checks a code by reading it out
/// to the pilot team; there is no partner portal yet. Sample offers get codes marked SAMPLE.
/// </summary>
internal sealed class LocalRewardCatalogProvider(CouplesDbContext db, IClock clock, IOptions<RewardsOptions> options) : IRewardCatalogProvider
{
    public const string ProviderName = "Local";

    public string Name => ProviderName;

    public async Task<IReadOnlyList<RewardOffer>> ListOffersAsync(string? area, CancellationToken cancellationToken = default)
    {
        var active = await db.RewardOffers.AsNoTracking()
            .Where(o => o.IsActive)
            .OrderBy(o => o.CostCredits).ThenBy(o => o.Title)
            .ToListAsync(cancellationToken);
        return [.. active.Where(o => o.AvailableIn(area))];
    }

    public Task<RewardOffer?> FindOfferAsync(Guid offerId, CancellationToken cancellationToken = default) =>
        db.RewardOffers.AsNoTracking().FirstOrDefaultAsync(o => o.Id == offerId, cancellationToken);

    public Task<Result<IssuedVoucher>> IssueAsync(RewardOffer offer, Guid redemptionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(offer);

        // Derived from the redemption id, so asking twice for the same redemption gives the same code.
        var hash = SHA256.HashData(redemptionId.ToByteArray());
        var code = Convert.ToHexString(hash, 0, 4) + "-" + Convert.ToHexString(hash, 4, 4);
        var expires = clock.UtcNow.AddDays(Math.Max(1, options.Value.VoucherValidDays));
        var voucher = offer.IsSample
            ? new IssuedVoucher($"SAMPLE-{code}", "This is a sample reward for trying BridgeTogether. It can't be used at a real business.", expires)
            : new IssuedVoucher($"BT-{code}", $"Show this code to {offer.PartnerName} when you book or pay.", expires);
        return Task.FromResult(Result.Ok(voucher));
    }
}
