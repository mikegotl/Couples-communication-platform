using Couples.Domain.Common;
using Couples.Domain.Rewards;

namespace Couples.Application.Rewards;

/// <summary>
/// Where date rewards come from and how a voucher is issued. The built-in provider uses the
/// admin-managed catalog and issues its own codes. A real integration (a gift-card API, or a
/// local-partner platform) implements this interface and is selected with <c>Rewards:Provider</c>;
/// see ADR-0017 for what it must guarantee.
/// </summary>
public interface IRewardCatalogProvider
{
    /// <summary>Stable name stored on each redemption.</summary>
    string Name { get; }

    /// <summary>Active offers a couple in <paramref name="area"/> can choose, cheapest first. Null area means anywhere-only offers.</summary>
    Task<IReadOnlyList<RewardOffer>> ListOffersAsync(string? area, CancellationToken cancellationToken = default);

    Task<RewardOffer?> FindOfferAsync(Guid offerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Issues a voucher. <paramref name="redemptionId"/> is the idempotency key: calling again with
    /// the same id must return the same voucher, never a second one. Must not receive names,
    /// emails or anything about the couple beyond this id.
    /// </summary>
    Task<Result<IssuedVoucher>> IssueAsync(RewardOffer offer, Guid redemptionId, CancellationToken cancellationToken = default);
}

public sealed record IssuedVoucher(string Code, string Instructions, DateTimeOffset? ExpiresAt);

/// <summary>Configuration under <c>Rewards</c>.</summary>
public sealed class RewardsOptions
{
    public const string SectionName = "Rewards";

    /// <summary>"Local" (admin-managed catalog). Other providers plug in here.</summary>
    public string Provider { get; set; } = "Local";

    /// <summary>Adds clearly labelled sample offers when the catalog is empty. Development only.</summary>
    public bool SeedSampleCatalog { get; set; }

    /// <summary>How long a locally issued voucher is valid.</summary>
    public int VoucherValidDays { get; set; } = 90;

    /// <summary>Partner applications accepted per day across the site, to keep spam out of the review queue.</summary>
    public int PartnerApplicationsPerDay { get; set; } = 50;
}

/// <summary>Admin management of the local catalog. Checks the Admin role itself.</summary>
public interface IRewardCatalogAdminService
{
    Task<Result<RewardCatalogAdminView>> GetAsync(Guid adminUserId, CancellationToken cancellationToken = default);

    Task<Result<Guid>> CreateAsync(Guid adminUserId, RewardOfferDetails details, CancellationToken cancellationToken = default);

    Task<Result> UpdateAsync(Guid adminUserId, Guid offerId, RewardOfferDetails details, CancellationToken cancellationToken = default);

    Task<Result> SetActiveAsync(Guid adminUserId, Guid offerId, bool active, CancellationToken cancellationToken = default);
}

public sealed record RewardCatalogAdminView(bool FlagOn, string Provider, IReadOnlyList<AdminRewardOffer> Offers);

public sealed record AdminRewardOffer(Guid Id, RewardOfferDetails Details, bool IsActive, int TimesRedeemed);
