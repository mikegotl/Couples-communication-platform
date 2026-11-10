using Couples.Application.Abstractions;
using Couples.Application.Rewards;
using Couples.Domain.Common;
using Couples.Domain.Rewards;
using Couples.Domain.Workspaces;
using Couples.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Couples.Infrastructure.Rewards;

/// <summary>
/// Partner applications and the partner's voucher check. Logs nothing: applications carry contact
/// details and check links are secrets.
/// </summary>
internal sealed class PartnerService(CouplesDbContext db, IClock clock, IOptions<RewardsOptions> options) : IPartnerService
{
    public async Task<Result> ApplyAsync(PartnerApplication application, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        var now = clock.UtcNow;
        var created = RewardPartner.Apply(application, now);
        if (!created.Succeeded)
        {
            return Result.Fail(created.Error!.Value);
        }

        var since = now.AddDays(-1);
        if (await db.RewardPartners.CountAsync(p => p.CreatedAt >= since, cancellationToken) >= options.Value.PartnerApplicationsPerDay)
        {
            return Result.Fail(DomainError.RateLimited);
        }

        // The same business applying again while its first application is waiting changes nothing.
        var email = created.Value.ContactEmail;
        var name = created.Value.BusinessName;
        if (!await db.RewardPartners.AnyAsync(p => p.Status == PartnerStatus.Pending && p.ContactEmail == email && p.BusinessName == name, cancellationToken))
        {
            db.RewardPartners.Add(created.Value);
            await db.SaveChangesAsync(cancellationToken);
        }

        return Result.Ok();
    }

    public async Task<string?> PartnerNameAsync(string? checkKey, CancellationToken cancellationToken = default) =>
        (await FindPartnerAsync(checkKey, cancellationToken))?.BusinessName;

    public async Task<VoucherCheck> CheckVoucherAsync(string? checkKey, string? voucherCode, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        var redemption = await FindRedemptionAsync(checkKey, voucherCode, cancellationToken);
        return redemption is null ? new VoucherCheck(VoucherState.NotFound) : ToCheck(redemption, clock.UtcNow);
    }

    public async Task<VoucherCheck> MarkUsedAsync(string? checkKey, string? voucherCode, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        var redemption = await FindRedemptionAsync(checkKey, voucherCode, cancellationToken);
        if (redemption is null)
        {
            return new VoucherCheck(VoucherState.NotFound);
        }

        var now = clock.UtcNow;
        if (redemption.MarkUsed(now))
        {
            // Two tills marking the same code at once: only one update applies.
            var updated = await db.RewardRedemptions
                .Where(r => r.Id == redemption.Id && r.UsedAt == null)
                .ExecuteUpdateAsync(set => set.SetProperty(r => r.UsedAt, now).SetProperty(r => r.UpdatedAt, now), cancellationToken);
            if (updated == 0)
            {
                db.ChangeTracker.Clear();
                redemption = await db.RewardRedemptions.AsNoTracking().FirstAsync(r => r.Id == redemption.Id, cancellationToken);
            }
        }

        return ToCheck(redemption, now);
    }

    private static VoucherCheck ToCheck(RewardRedemption r, DateTimeOffset now)
    {
        var state = r.UsedAt is not null ? VoucherState.Used
            : r.ExpiresAt is { } expires && expires < now ? VoucherState.Expired
            : VoucherState.Valid;
        return new VoucherCheck(state, r.OfferTitle, r.ExpiresAt, r.UsedAt);
    }

    private async Task<RewardPartner?> FindPartnerAsync(string? checkKey, CancellationToken cancellationToken)
    {
        if (!InvitationToken.LooksValid(checkKey))
        {
            return null;
        }

        var hash = InvitationToken.Hash(checkKey!);
        return await db.RewardPartners.AsNoTracking()
            .FirstOrDefaultAsync(p => p.CheckKeyHash == hash && p.Status == PartnerStatus.Approved, cancellationToken);
    }

    private async Task<RewardRedemption?> FindRedemptionAsync(string? checkKey, string? voucherCode, CancellationToken cancellationToken)
    {
        var code = voucherCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code) || code.Length > RewardRedemption.MaxVoucherLength)
        {
            return null;
        }

        var partner = await FindPartnerAsync(checkKey, cancellationToken);
        if (partner is null)
        {
            return null;
        }

        return await db.RewardRedemptions.AsNoTracking()
            .Where(r => r.VoucherCode == code && r.Status == RedemptionStatus.Issued
                && db.RewardOffers.Any(o => o.Id == r.OfferId && o.PartnerId == partner.Id))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
