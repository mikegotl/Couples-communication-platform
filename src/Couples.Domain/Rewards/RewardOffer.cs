using Couples.Domain.Common;
using Couples.Domain.Privacy;

namespace Couples.Domain.Rewards;

/// <summary>
/// A date reward couples can choose, such as dinner for two or a gift card. Managed by admins in
/// the local catalog; a provider such as a gift-card API can supply its own (ADR-0017).
/// </summary>
[Privacy(DataClassification.Internal, "Reference data managed by admins; kept while any redemption refers to it, otherwise until an admin removes it.")]
public sealed class RewardOffer : Entity
{
    public const int MaxTitleLength = 80;
    public const int MaxPartnerLength = 80;
    public const int MaxDescriptionLength = 400;
    public const int MaxAreaLength = CoupleRewardAccount.MaxAreaLength;
    public const int MinCost = 5;
    public const int MaxCost = 1_000;

    private RewardOffer()
    {
    }

    public string Title { get; private set; } = string.Empty;

    /// <summary>The business or provider honouring it, for example a restaurant's name.</summary>
    public string PartnerName { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    /// <summary>Town or area it is available in, or null for anywhere (for example an online gift card).</summary>
    public string? Area { get; private set; }

    public RewardKind Kind { get; private set; }

    public int CostCredits { get; private set; }

    /// <summary>A demonstration offer with no real business behind it. Its vouchers say so.</summary>
    public bool IsSample { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>The approved business honouring it, if it came from a partner application. Their check link can confirm its codes.</summary>
    public Guid? PartnerId { get; private set; }

    public static Result<RewardOffer> CreateFor(RewardPartner partner, string? title, string? description, int costCredits, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(partner);
        var created = Create(new RewardOfferDetails(title, partner.BusinessName, description, partner.Area, partner.Kind, costCredits, IsSample: false), now);
        if (created.Succeeded)
        {
            created.Value.PartnerId = partner.Id;
        }

        return created;
    }

    public static Result<RewardOffer> Create(RewardOfferDetails details, DateTimeOffset now)
    {
        var offer = new RewardOffer { IsActive = true };
        var applied = offer.Apply(details, now);
        return applied.Succeeded ? Result.Ok(offer) : applied.Error!.Value;
    }

    public Result Update(RewardOfferDetails details, DateTimeOffset now) => Apply(details, now);

    public void SetActive(bool active, DateTimeOffset now)
    {
        IsActive = active;
        Touch(now);
    }

    /// <summary>Whether a couple who set <paramref name="area"/> can choose it.</summary>
    public bool AvailableIn(string? area) =>
        Area is null || (area is not null && string.Equals(Area.Trim(), area.Trim(), StringComparison.OrdinalIgnoreCase));

    private Result Apply(RewardOfferDetails details, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(details);
        var title = details.Title?.Trim() ?? string.Empty;
        var partner = details.PartnerName?.Trim() ?? string.Empty;
        var description = details.Description?.Trim() ?? string.Empty;
        var area = string.IsNullOrWhiteSpace(details.Area) ? null : details.Area.Trim();
        if (title.Length is 0 or > MaxTitleLength
            || partner.Length is 0 or > MaxPartnerLength
            || description.Length > MaxDescriptionLength
            || area is { Length: > MaxAreaLength }
            || details.CostCredits is < MinCost or > MaxCost
            || !Enum.IsDefined(details.Kind))
        {
            return Result.Fail(DomainError.Validation);
        }

        Title = title;
        PartnerName = partner;
        Description = description;
        Area = area;
        Kind = details.Kind;
        CostCredits = details.CostCredits;
        IsSample = details.IsSample;
        Touch(now);
        return Result.Ok();
    }
}

public sealed record RewardOfferDetails(string? Title, string? PartnerName, string? Description, string? Area, RewardKind Kind, int CostCredits, bool IsSample);

public enum RewardKind
{
    Restaurant = 1,
    Experience = 2,
    GiftCard = 3,
}
