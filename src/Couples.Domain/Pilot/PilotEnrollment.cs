using Couples.Domain.Common;
using Couples.Domain.Privacy;

namespace Couples.Domain.Pilot;

/// <summary>
/// The versions of the pilot documents a participant must agree to. Changing either text means
/// bumping its version, and everyone is asked to agree again before continuing.
/// </summary>
public static class PilotDocuments
{
    public const string TermsVersion = "pilot-terms-2026-11-draft";

    public const string PrivacyNoticeVersion = "privacy-notice-2026-11-draft";
}

/// <summary>
/// A user's informed, revocable agreement to take part in the private pilot. Holds which
/// document versions they agreed to and when; withdrawing keeps the record (as evidence of what
/// was agreed) until the account is deleted.
/// </summary>
[Privacy(DataClassification.Personal, "Kept while the account exists as evidence of consent; deleted with the account.")]
public sealed class PilotEnrollment
{
    private PilotEnrollment()
    {
    }

    public Guid UserId { get; private set; }

    public string TermsVersion { get; private set; } = string.Empty;

    public string PrivacyNoticeVersion { get; private set; } = string.Empty;

    public DateTimeOffset ConsentedAt { get; private set; }

    public DateTimeOffset? WithdrawnAt { get; private set; }

    /// <summary>Enrolled now, on the current documents.</summary>
    public bool IsCurrent => WithdrawnAt is null && TermsVersion == PilotDocuments.TermsVersion && PrivacyNoticeVersion == PilotDocuments.PrivacyNoticeVersion;

    public static Result<PilotEnrollment> Create(Guid userId, string termsVersion, string privacyNoticeVersion, DateTimeOffset now)
    {
        var enrollment = new PilotEnrollment { UserId = userId };
        var consented = enrollment.Consent(termsVersion, privacyNoticeVersion, now);
        return consented.Succeeded ? Result.Ok(enrollment) : consented.Error!.Value;
    }

    /// <summary>Agree (again) to the current documents. The versions must be the ones the user was shown.</summary>
    public Result Consent(string termsVersion, string privacyNoticeVersion, DateTimeOffset now)
    {
        if (termsVersion != PilotDocuments.TermsVersion || privacyNoticeVersion != PilotDocuments.PrivacyNoticeVersion)
        {
            return Result.Fail(DomainError.ConsentMismatch);
        }

        TermsVersion = termsVersion;
        PrivacyNoticeVersion = privacyNoticeVersion;
        ConsentedAt = now;
        WithdrawnAt = null;
        return Result.Ok();
    }

    public void Withdraw(DateTimeOffset now) => WithdrawnAt ??= now;
}
