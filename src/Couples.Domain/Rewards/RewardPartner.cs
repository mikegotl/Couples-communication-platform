using System.Net.Mail;
using Couples.Domain.Common;
using Couples.Domain.Privacy;
using Couples.Domain.Workspaces;

namespace Couples.Domain.Rewards;

/// <summary>
/// A local business that asked to offer date rewards ("Partner with us"), and the admin's
/// decision. Nothing is shown to couples until an admin approves it and an offer is created.
/// An approved partner gets a private check link for confirming and using up voucher codes;
/// only the link's SHA-256 hash is stored.
/// </summary>
[Privacy(DataClassification.Personal, "Business contact details. Kept while the partnership is active; declined applications are purged after 180 days.")]
public sealed class RewardPartner : Entity
{
    public const int MaxNameLength = 80;
    public const int MaxEmailLength = 254;
    public const int MaxPhoneLength = 30;
    public const int MaxWebsiteLength = 200;
    public const int MaxOfferLength = 400;

    private RewardPartner()
    {
    }

    public string BusinessName { get; private set; } = string.Empty;

    public string ContactName { get; private set; } = string.Empty;

    public string ContactEmail { get; private set; } = string.Empty;

    public string? Phone { get; private set; }

    public string? Website { get; private set; }

    public string Area { get; private set; } = string.Empty;

    public RewardKind Kind { get; private set; }

    /// <summary>What the business said it could offer couples, in its own words.</summary>
    public string ProposedOffer { get; private set; } = string.Empty;

    public PartnerStatus Status { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    public string? CheckKeyHash { get; private set; }

    public static Result<RewardPartner> Apply(PartnerApplication application, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(application);
        var business = Clean(application.BusinessName);
        var contact = Clean(application.ContactName);
        var email = Clean(application.ContactEmail);
        var phone = NullIfEmpty(application.Phone);
        var website = NullIfEmpty(application.Website);
        var area = Clean(application.Area);
        var offer = Clean(application.ProposedOffer);
        if (business.Length is 0 or > MaxNameLength
            || contact.Length is 0 or > MaxNameLength
            || email.Length > MaxEmailLength || !MailAddress.TryCreate(email, out _) || email.Contains(' ', StringComparison.Ordinal)
            || phone is { Length: > MaxPhoneLength }
            || (website is not null && !IsWebAddress(website))
            || area.Length is 0 or > CoupleRewardAccount.MaxAreaLength
            || offer.Length is 0 or > MaxOfferLength
            || !Enum.IsDefined(application.Kind))
        {
            return DomainError.Validation;
        }

        var partner = new RewardPartner
        {
            BusinessName = business,
            ContactName = contact,
            ContactEmail = email,
            Phone = phone,
            Website = website,
            Area = area,
            Kind = application.Kind,
            ProposedOffer = offer,
            Status = PartnerStatus.Pending,
        };
        partner.Touch(now);
        return Result.Ok(partner);
    }

    /// <summary>Approves the partner and returns a new check link key, shown to the admin once.</summary>
    public string Approve(DateTimeOffset now)
    {
        Status = PartnerStatus.Approved;
        DecidedAt = now;
        return NewCheckKey(now);
    }

    public void Decline(DateTimeOffset now)
    {
        Status = PartnerStatus.Declined;
        DecidedAt = now;
        CheckKeyHash = null;
        Touch(now);
    }

    /// <summary>Replaces the check link; the old one stops working.</summary>
    public string NewCheckKey(DateTimeOffset now)
    {
        var key = InvitationToken.Generate();
        CheckKeyHash = InvitationToken.Hash(key);
        Touch(now);
        return key;
    }

    public static bool IsWebAddress(string value) =>
        value.Length <= MaxWebsiteLength
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static string Clean(string? value) => value?.Trim() ?? string.Empty;

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record PartnerApplication(string? BusinessName, string? ContactName, string? ContactEmail, string? Phone, string? Website, string? Area, RewardKind Kind, string? ProposedOffer);

public enum PartnerStatus
{
    Pending = 1,
    Approved = 2,
    Declined = 3,
}
