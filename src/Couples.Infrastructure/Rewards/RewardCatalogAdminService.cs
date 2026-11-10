using Couples.Application;
using Couples.Application.Abstractions;
using Couples.Application.Rewards;
using Couples.Domain.Common;
using Couples.Domain.Rewards;
using Couples.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Couples.Infrastructure.Rewards;

/// <summary>Manages the local reward catalog. Checks the Admin role itself; shows counts, never which couple redeemed what.</summary>
internal sealed class RewardCatalogAdminService(CouplesDbContext db, IClock clock, IFeatureFlags flags, IOptions<RewardsOptions> options) : IRewardCatalogAdminService
{
    private const string AdminRole = "ADMIN";

    public async Task<Result<RewardCatalogAdminView>> GetAsync(Guid adminUserId, CancellationToken cancellationToken = default)
    {
        if (!await IsAdminAsync(adminUserId, cancellationToken))
        {
            return DomainError.NotFound;
        }

        var offers = await db.RewardOffers.AsNoTracking()
            .OrderByDescending(o => o.IsActive).ThenBy(o => o.CostCredits).ThenBy(o => o.Title)
            .Select(o => new
            {
                Offer = o,
                Redeemed = db.RewardRedemptions.Count(r => r.OfferId == o.Id && r.Status == RedemptionStatus.Issued),
            })
            .ToListAsync(cancellationToken);
        var rows = offers
            .Select(x => new AdminRewardOffer(
                x.Offer.Id,
                new RewardOfferDetails(x.Offer.Title, x.Offer.PartnerName, x.Offer.Description, x.Offer.Area, x.Offer.Kind, x.Offer.CostCredits, x.Offer.IsSample),
                x.Offer.IsActive,
                x.Redeemed))
            .ToList();
        var flagOn = await flags.IsEnabledAsync(FeatureFlags.DateRewards, cancellationToken);
        return Result.Ok(new RewardCatalogAdminView(flagOn, options.Value.Provider, rows));
    }

    public async Task<Result<Guid>> CreateAsync(Guid adminUserId, RewardOfferDetails details, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        if (!await IsAdminAsync(adminUserId, cancellationToken))
        {
            return DomainError.NotFound;
        }

        var created = RewardOffer.Create(details, clock.UtcNow);
        if (!created.Succeeded)
        {
            return created.Error!.Value;
        }

        db.RewardOffers.Add(created.Value);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Ok(created.Value.Id);
    }

    public async Task<Result> UpdateAsync(Guid adminUserId, Guid offerId, RewardOfferDetails details, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        var offer = await FindAsync(adminUserId, offerId, cancellationToken);
        if (offer is null)
        {
            return Result.Fail(DomainError.NotFound);
        }

        var updated = offer.Update(details, clock.UtcNow);
        if (updated.Succeeded)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return updated;
    }

    public async Task<Result> SetActiveAsync(Guid adminUserId, Guid offerId, bool active, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        var offer = await FindAsync(adminUserId, offerId, cancellationToken);
        if (offer is null)
        {
            return Result.Fail(DomainError.NotFound);
        }

        offer.SetActive(active, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }

    private async Task<RewardOffer?> FindAsync(Guid adminUserId, Guid offerId, CancellationToken cancellationToken) =>
        await IsAdminAsync(adminUserId, cancellationToken)
            ? await db.RewardOffers.FirstOrDefaultAsync(o => o.Id == offerId, cancellationToken)
            : null;

    private Task<bool> IsAdminAsync(Guid userId, CancellationToken cancellationToken) =>
        db.UserRoles.AnyAsync(ur => ur.UserId == userId && db.Roles.Any(r => r.Id == ur.RoleId && r.NormalizedName == AdminRole), cancellationToken);
}
