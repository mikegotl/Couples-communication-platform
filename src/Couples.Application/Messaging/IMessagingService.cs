using Couples.Domain.Common;

namespace Couples.Application.Messaging;

/// <summary>
/// Drafts (private to their author) and explicit, per-message sharing with a partner.
/// Nothing is shared without a call to <see cref="ShareAsync"/> carrying the fingerprint of the
/// exact text the author approved. Every method checks ownership and active membership itself.
/// </summary>
public interface IMessagingService
{
    Task<Result<DraftView>> CreateDraftAsync(Guid userId, string? body, CancellationToken cancellationToken = default);

    Task<Result<DraftView>> EditDraftAsync(Guid userId, Guid draftId, string? body, CancellationToken cancellationToken = default);

    Task<Result<DraftView>> GetDraftAsync(Guid userId, Guid draftId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DraftView>> ListDraftsAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Result> DiscardDraftAsync(Guid userId, Guid draftId, CancellationToken cancellationToken = default);

    /// <summary>
    /// What the partner would see if the draft were shared now, plus the fingerprint the author
    /// must send back to approve exactly this text. Fails with NoPartner when nobody can receive it, or PartnerPaused while the partner has paused messages, or SafetyHold while the author's own safety settings stop sharing.
    /// </summary>
    Task<Result<SharePreview>> PreviewShareAsync(Guid userId, Guid draftId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Shares a draft with the user's partner. <paramref name="approvedFingerprint"/> must equal the
    /// <see cref="DraftView.Fingerprint"/> the user saw in the preview; records a consent ledger
    /// entry and an audit event. Idempotent: repeating the call for a draft that was already
    /// shared with the same approval returns the original message and records nothing new.
    /// </summary>
    Task<Result<SharedMessageView>> ShareAsync(Guid userId, Guid draftId, string approvedFingerprint, CancellationToken cancellationToken = default);

    /// <summary>Messages shared with the user by their current partner, newest first. Revoked ones are excluded.</summary>
    Task<IReadOnlyList<SharedMessageView>> InboxAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Messages the user has shared, newest first, including revoked ones (without text).</summary>
    Task<IReadOnlyList<SharedMessageView>> SentAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Result<SharedMessageView>> GetSharedAsync(Guid userId, Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>Sender only: hides the message from the recipient and erases its text.</summary>
    Task<Result> RevokeAsync(Guid userId, Guid messageId, CancellationToken cancellationToken = default);
}

public sealed record DraftView(Guid Id, string Body, string Status, string Fingerprint, DateTimeOffset UpdatedAt, Guid? InReplyToMessageId = null);

public sealed record SharePreview(Guid DraftId, string Body, string Fingerprint, string WorkspaceName);

public sealed record SharedMessageView(Guid Id, bool SentByMe, string Body, DateTimeOffset SharedAt, bool Revoked);

/// <summary>Limits that slow down rapid back-and-forth during conflict (see ADR-0013).</summary>
public static class MessagingLimits
{
    /// <summary>Messages one person can send in any rolling hour.</summary>
    public const int SharesPerHour = 10;
}
