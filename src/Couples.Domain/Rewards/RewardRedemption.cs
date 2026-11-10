using Couples.Domain.Common;
using Couples.Domain.Privacy;

namespace Couples.Domain.Rewards;

/// <summary>
/// A reward a couple cashed credits in for, with the voucher the provider issued. Keeps a copy of
/// the offer's title and partner so the voucher still makes sense if the offer changes.
/// </summary>
[Privacy(DataClassification.Personal, "Kept while the workspace exists so both partners can find their voucher; deleted with the workspace. The redeeming partner's id is unlinked if they delete their account.")]
public sealed class RewardRedemption : Entity
{
    public const int MaxVoucherLength = 120;

    private RewardRedemption()
    {
    }

    public Guid WorkspaceId { get; private set; }

    public Guid OfferId { get; private set; }

    public Guid RedeemedByUserId { get; private set; }

    public string OfferTitle { get; private set; } = string.Empty;

    public string PartnerName { get; private set; } = string.Empty;

    public int CostCredits { get; private set; }

    public string Provider { get; private set; } = string.Empty;

    public string VoucherCode { get; private set; } = string.Empty;

    public string Instructions { get; private set; } = string.Empty;

    public bool IsSample { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <summary>
    /// Credits are reserved (Pending) before the provider is asked for a voucher, so a provider
    /// that charges money is never called for credits the couple doesn't have.
    /// </summary>
    public RedemptionStatus Status { get; private set; }

    public static RewardRedemption Reserve(Guid workspaceId, RewardOffer offer, Guid redeemedByUserId, string provider, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(offer);
        var redemption = new RewardRedemption
        {
            WorkspaceId = workspaceId,
            OfferId = offer.Id,
            RedeemedByUserId = redeemedByUserId,
            OfferTitle = offer.Title,
            PartnerName = offer.PartnerName,
            CostCredits = offer.CostCredits,
            Provider = provider,
            IsSample = offer.IsSample,
            Status = RedemptionStatus.Pending,
        };
        redemption.Touch(now);
        return redemption;
    }

    public void Issue(string voucherCode, string instructions, DateTimeOffset? expiresAt, DateTimeOffset now)
    {
        VoucherCode = voucherCode.Length > MaxVoucherLength ? voucherCode[..MaxVoucherLength] : voucherCode;
        Instructions = instructions.Length > 400 ? instructions[..400] : instructions;
        ExpiresAt = expiresAt;
        Status = RedemptionStatus.Issued;
        Touch(now);
    }
}

public enum RedemptionStatus
{
    Pending = 1,
    Issued = 2,
}
