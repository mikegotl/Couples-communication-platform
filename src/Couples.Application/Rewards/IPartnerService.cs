using Couples.Domain.Common;
using Couples.Domain.Rewards;

namespace Couples.Application.Rewards;

/// <summary>
/// The public side of reward partnerships (ADR-0018): a business applies, and an approved
/// business uses its private check link to confirm a couple's voucher and mark it used. The check
/// link never reveals who the couple is.
/// </summary>
public interface IPartnerService
{
    /// <summary>Records an application for an admin to review. Fails with RateLimited when too many arrive in a day.</summary>
    Task<Result> ApplyAsync(PartnerApplication application, CancellationToken cancellationToken = default);

    /// <summary>The business a check link belongs to, or null if the link is unknown or no longer valid.</summary>
    Task<string?> PartnerNameAsync(string? checkKey, CancellationToken cancellationToken = default);

    /// <summary>Looks a voucher code up among this partner's own offers. Unknown codes and other partners' codes look the same.</summary>
    Task<VoucherCheck> CheckVoucherAsync(string? checkKey, string? voucherCode, CancellationToken cancellationToken = default);

    /// <summary>Marks a valid voucher as used, once.</summary>
    Task<VoucherCheck> MarkUsedAsync(string? checkKey, string? voucherCode, CancellationToken cancellationToken = default);
}

public sealed record VoucherCheck(VoucherState State, string? OfferTitle = null, DateTimeOffset? ExpiresAt = null, DateTimeOffset? UsedAt = null);

public enum VoucherState
{
    NotFound,
    Valid,
    Used,
    Expired,
}

/// <summary>Admin review of partner applications. Checks the Admin role itself.</summary>
public interface IPartnerAdminService
{
    Task<Result<IReadOnlyList<AdminPartner>>> ListAsync(Guid adminUserId, CancellationToken cancellationToken = default);

    /// <summary>Approves the application and creates its first offer. Returns the check link key, shown once.</summary>
    Task<Result<string>> ApproveAsync(Guid adminUserId, Guid partnerId, string? offerTitle, string? offerDescription, int costCredits, CancellationToken cancellationToken = default);

    Task<Result> DeclineAsync(Guid adminUserId, Guid partnerId, CancellationToken cancellationToken = default);

    /// <summary>Replaces an approved partner's check link. The old link stops working.</summary>
    Task<Result<string>> NewCheckKeyAsync(Guid adminUserId, Guid partnerId, CancellationToken cancellationToken = default);
}

public sealed record AdminPartner(Guid Id, PartnerApplication Application, PartnerStatus Status, DateTimeOffset AppliedAt, int Offers, int VouchersIssued, int VouchersUsed);

/// <summary>
/// Where "date ideas near you" come from. The built-in provider reads the admin-managed list. A
/// places service (for example Google Places) can implement this later; it must be given only the
/// town or area, never anything else about the couple.
/// </summary>
public interface IDateIdeaProvider
{
    /// <summary>Up to <paramref name="max"/> active ideas for the area, local ones first.</summary>
    Task<IReadOnlyList<DateIdeaView>> ListAsync(string? area, int max, CancellationToken cancellationToken = default);
}

public sealed record DateIdeaView(Guid Id, string Title, string Description, string? Area, DateIdeaKind Kind, string? Url);

public interface IDateIdeaAdminService
{
    Task<Result<IReadOnlyList<AdminDateIdea>>> ListAsync(Guid adminUserId, CancellationToken cancellationToken = default);

    Task<Result<Guid>> CreateAsync(Guid adminUserId, DateIdeaDetails details, CancellationToken cancellationToken = default);

    Task<Result> SetActiveAsync(Guid adminUserId, Guid ideaId, bool active, CancellationToken cancellationToken = default);
}

public sealed record AdminDateIdea(Guid Id, DateIdeaDetails Details, bool IsActive);
