using Couples.Domain.Common;
using Couples.Domain.Privacy;

namespace Couples.Domain.Sms;

/// <summary>
/// One user's choice to get their partner's messages by text, and to answer by text (ADR-0021).
/// Off until the user proves they hold the phone with a one-time code and agrees to texts. A STOP
/// reply turns texts off at once. Holds the phone number and, while a reply is waiting for the
/// user's SEND, the id and fingerprint of that draft; never any message text.
/// </summary>
[Privacy(DataClassification.Personal, "Kept while texts are set up; deleted when the user turns texts off or deletes their account.")]
public sealed class SmsSettings
{
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan CodeWindow = TimeSpan.FromHours(1);

    public static readonly TimeSpan ConfirmWindow = TimeSpan.FromMinutes(30);

    public const int MaxCodesPerWindow = 3;

    public const int MaxCodeAttempts = 5;

    private SmsSettings()
    {
    }

    public Guid UserId { get; private set; }

    /// <summary>The number in E.164 form (for example +15551234567). Verified once <see cref="VerifiedAt"/> is set.</summary>
    public string PhoneE164 { get; private set; } = string.Empty;

    public DateTimeOffset? VerifiedAt { get; private set; }

    /// <summary>Only messages that arrive from this time are texted: set when texts are turned on, or back on after STOP.</summary>
    public DateTimeOffset? ReceivingSince { get; private set; }

    /// <summary>True to put the message itself in the text; false for a notice that something arrived.</summary>
    public bool IncludeMessageText { get; private set; }

    /// <summary>When the user replied STOP (or the provider reported an opt-out). Nothing is sent until they opt in again.</summary>
    public DateTimeOffset? OptedOutAt { get; private set; }

    /// <summary>SHA-256 of the current one-time code (see <see cref="HashCode(Guid, string, string)"/>).</summary>
    public string? CodeHash { get; private set; }

    public DateTimeOffset? CodeExpiresAt { get; private set; }

    public int CodeAttempts { get; private set; }

    public DateTimeOffset? CodeWindowStart { get; private set; }

    public int CodesInWindow { get; private set; }

    /// <summary>A draft created from a text reply, waiting for the user to reply SEND.</summary>
    public Guid? PendingDraftId { get; private set; }

    /// <summary>Fingerprint of the exact text echoed back to the user for <see cref="PendingDraftId"/>.</summary>
    public string? PendingFingerprint { get; private set; }

    public DateTimeOffset? PendingExpiresAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsVerified => VerifiedAt is not null;

    /// <summary>Texts may be sent to this number.</summary>
    public bool CanReceive => IsVerified && OptedOutAt is null;

    public static SmsSettings Start(Guid userId, DateTimeOffset now) => new() { UserId = userId, CreatedAt = now, UpdatedAt = now };

    /// <summary>The hash stored for a code. Bound to the user and number so a code can't be reused elsewhere.</summary>
    public static string HashCode(Guid userId, string phoneE164, string code) =>
        ContentFingerprint.Of($"{userId:N}|{phoneE164}|{code}");

    /// <summary>
    /// Records a new code for <paramref name="phoneE164"/>. A different number starts again unverified.
    /// At most <see cref="MaxCodesPerWindow"/> codes an hour.
    /// </summary>
    public Result IssueCode(string phoneE164, string code, bool includeMessageText, DateTimeOffset now)
    {
        if (CodeWindowStart is not { } start || now - start >= CodeWindow)
        {
            CodeWindowStart = now;
            CodesInWindow = 0;
        }

        if (CodesInWindow >= MaxCodesPerWindow)
        {
            return Result.Fail(DomainError.RateLimited);
        }

        if (!string.Equals(PhoneE164, phoneE164, StringComparison.Ordinal))
        {
            PhoneE164 = phoneE164;
            VerifiedAt = null;
            ClearPending();
        }

        IncludeMessageText = includeMessageText;
        CodesInWindow++;
        CodeHash = HashCode(UserId, phoneE164, code);
        CodeExpiresAt = now + CodeLifetime;
        CodeAttempts = 0;
        UpdatedAt = now;
        return Result.Ok();
    }

    /// <summary>Checks a code. Five wrong tries or an expired code means asking for a new one.</summary>
    public Result Confirm(string? code, DateTimeOffset now)
    {
        if (CodeHash is null || CodeExpiresAt is not { } expires || now >= expires || CodeAttempts >= MaxCodeAttempts)
        {
            return Result.Fail(DomainError.InvitationInvalid);
        }

        var candidate = HashCode(UserId, PhoneE164, (code ?? string.Empty).Trim());
        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(candidate),
                System.Text.Encoding.ASCII.GetBytes(CodeHash)))
        {
            CodeAttempts++;
            UpdatedAt = now;
            return Result.Fail(DomainError.Validation);
        }

        VerifiedAt = now;
        ReceivingSince = now;
        OptedOutAt = null;
        CodeHash = null;
        CodeExpiresAt = null;
        CodeAttempts = 0;
        UpdatedAt = now;
        return Result.Ok();
    }

    public void SetIncludeMessageText(bool include, DateTimeOffset now)
    {
        IncludeMessageText = include;
        UpdatedAt = now;
    }

    /// <summary>STOP: nothing more is sent, and a waiting reply is dropped (it stays a private draft).</summary>
    public void OptOut(DateTimeOffset now)
    {
        OptedOutAt ??= now;
        ClearPending();
        UpdatedAt = now;
    }

    /// <summary>START after STOP. Only a verified number can opt back in.</summary>
    public bool OptIn(DateTimeOffset now)
    {
        if (!IsVerified)
        {
            return false;
        }

        if (OptedOutAt is not null)
        {
            ReceivingSince = now;
        }

        OptedOutAt = null;
        UpdatedAt = now;
        return true;
    }

    public void AwaitConfirmation(Guid draftId, string fingerprint, DateTimeOffset now)
    {
        PendingDraftId = draftId;
        PendingFingerprint = fingerprint;
        PendingExpiresAt = now + ConfirmWindow;
        UpdatedAt = now;
    }

    /// <summary>The draft waiting for SEND, if it hasn't expired.</summary>
    public (Guid DraftId, string Fingerprint)? PendingConfirmation(DateTimeOffset now) =>
        PendingDraftId is { } id && PendingFingerprint is { } fingerprint && PendingExpiresAt is { } expires && now < expires
            ? (id, fingerprint)
            : null;

    public void ClearPending()
    {
        PendingDraftId = null;
        PendingFingerprint = null;
        PendingExpiresAt = null;
    }
}
