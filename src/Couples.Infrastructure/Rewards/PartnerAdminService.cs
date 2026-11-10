using Couples.Application.Abstractions;
using Couples.Application.Rewards;
using Couples.Domain.Common;
using Couples.Domain.Rewards;
using Couples.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Couples.Infrastructure.Rewards;

/// <summary>Admin review of partner applications and the date ideas list. Checks the Admin role itself.</summary>
internal sealed class PartnerAdminService(CouplesDbContext db, IClock clock) : IPartnerAdminService, IDateIdeaAdminService, IDateIdeaProvider
{
    private const string AdminRole = "ADMIN";

    public async Task<Result<IReadOnlyList<AdminPartner>>> ListAsync(Guid adminUserId, CancellationToken cancellationToken = default)
    {
        if (!await IsAdminAsync(adminUserId, cancellationToken))
        {
            return DomainError.NotFound;
        }

        var rows = await db.RewardPartners.AsNoTracking()
            .OrderBy(p => p.Status).ThenByDescending(p => p.CreatedAt)
            .Take(200)
            .Select(p => new
            {
                Partner = p,
                Offers = db.RewardOffers.Count(o => o.PartnerId == p.Id),
                Issued = db.RewardRedemptions.Count(r => r.Status == RedemptionStatus.Issued && db.RewardOffers.Any(o => o.Id == r.OfferId && o.PartnerId == p.Id)),
                Used = db.RewardRedemptions.Count(r => r.UsedAt != null && db.RewardOffers.Any(o => o.Id == r.OfferId && o.PartnerId == p.Id)),
            })
            .ToListAsync(cancellationToken);
        IReadOnlyList<AdminPartner> partners = [.. rows.Select(x => new AdminPartner(
            x.Partner.Id,
            new PartnerApplication(x.Partner.BusinessName, x.Partner.ContactName, x.Partner.ContactEmail, x.Partner.Phone, x.Partner.Website, x.Partner.Area, x.Partner.Kind, x.Partner.ProposedOffer),
            x.Partner.Status,
            x.Partner.CreatedAt,
            x.Offers,
            x.Issued,
            x.Used))];
        return Result.Ok(partners);
    }

    public async Task<Result<string>> ApproveAsync(Guid adminUserId, Guid partnerId, string? offerTitle, string? offerDescription, int costCredits, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        var partner = await FindPartnerAsync(adminUserId, partnerId, cancellationToken);
        if (partner is null)
        {
            return DomainError.NotFound;
        }

        if (partner.Status != PartnerStatus.Pending)
        {
            return DomainError.Conflict;
        }

        var now = clock.UtcNow;
        var offer = RewardOffer.CreateFor(partner, offerTitle, offerDescription, costCredits, now);
        if (!offer.Succeeded)
        {
            return offer.Error!.Value;
        }

        var key = partner.Approve(now);
        db.RewardOffers.Add(offer.Value);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Ok(key);
    }

    public async Task<Result> DeclineAsync(Guid adminUserId, Guid partnerId, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        var partner = await FindPartnerAsync(adminUserId, partnerId, cancellationToken);
        if (partner is null)
        {
            return Result.Fail(DomainError.NotFound);
        }

        var now = clock.UtcNow;
        partner.Decline(now);

        // A declined (or later removed) partner's offers stop being shown; vouchers already issued stay with the couples.
        await db.RewardOffers.Where(o => o.PartnerId == partnerId)
            .ExecuteUpdateAsync(set => set.SetProperty(o => o.IsActive, false).SetProperty(o => o.UpdatedAt, now), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<Result<string>> NewCheckKeyAsync(Guid adminUserId, Guid partnerId, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        var partner = await FindPartnerAsync(adminUserId, partnerId, cancellationToken);
        if (partner is null || partner.Status != PartnerStatus.Approved)
        {
            return DomainError.NotFound;
        }

        var key = partner.NewCheckKey(clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Ok(key);
    }

    async Task<Result<IReadOnlyList<AdminDateIdea>>> IDateIdeaAdminService.ListAsync(Guid adminUserId, CancellationToken cancellationToken)
    {
        if (!await IsAdminAsync(adminUserId, cancellationToken))
        {
            return DomainError.NotFound;
        }

        var ideas = await db.DateIdeas.AsNoTracking().OrderByDescending(i => i.IsActive).ThenBy(i => i.Area).ThenBy(i => i.Title).ToListAsync(cancellationToken);
        IReadOnlyList<AdminDateIdea> rows = [.. ideas.Select(i => new AdminDateIdea(i.Id, new DateIdeaDetails(i.Title, i.Description, i.Area, i.Kind, i.Url), i.IsActive))];
        return Result.Ok(rows);
    }

    public async Task<Result<Guid>> CreateAsync(Guid adminUserId, DateIdeaDetails details, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        if (!await IsAdminAsync(adminUserId, cancellationToken))
        {
            return DomainError.NotFound;
        }

        var created = DateIdea.Create(details, clock.UtcNow);
        if (!created.Succeeded)
        {
            return created.Error!.Value;
        }

        db.DateIdeas.Add(created.Value);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Ok(created.Value.Id);
    }

    public async Task<Result> SetActiveAsync(Guid adminUserId, Guid ideaId, bool active, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        var idea = await IsAdminAsync(adminUserId, cancellationToken)
            ? await db.DateIdeas.FirstOrDefaultAsync(i => i.Id == ideaId, cancellationToken)
            : null;
        if (idea is null)
        {
            return Result.Fail(DomainError.NotFound);
        }

        idea.SetActive(active, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }

    async Task<IReadOnlyList<DateIdeaView>> IDateIdeaProvider.ListAsync(string? area, int max, CancellationToken cancellationToken)
    {
        var active = await db.DateIdeas.AsNoTracking().Where(i => i.IsActive).OrderBy(i => i.Title).ToListAsync(cancellationToken);
        return [.. active
            .Where(i => i.AvailableIn(area))
            .OrderBy(i => i.Area is null) // local ideas first
            .Take(max)
            .Select(i => new DateIdeaView(i.Id, i.Title, i.Description, i.Area, i.Kind, i.Url))];
    }

    private async Task<RewardPartner?> FindPartnerAsync(Guid adminUserId, Guid partnerId, CancellationToken cancellationToken) =>
        await IsAdminAsync(adminUserId, cancellationToken)
            ? await db.RewardPartners.FirstOrDefaultAsync(p => p.Id == partnerId, cancellationToken)
            : null;

    private Task<bool> IsAdminAsync(Guid userId, CancellationToken cancellationToken) =>
        db.UserRoles.AnyAsync(ur => ur.UserId == userId && db.Roles.Any(r => r.Id == ur.RoleId && r.NormalizedName == AdminRole), cancellationToken);
}
