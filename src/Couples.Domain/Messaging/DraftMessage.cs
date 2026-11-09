using Couples.Domain.Common;
using Couples.Domain.Privacy;

namespace Couples.Domain.Messaging;

/// <summary>
/// A message being prepared for a partner. Private to its author until they explicitly share it;
/// sharing copies the approved text into a <see cref="SharedMessage"/>. The full preview, AI
/// rewrite and delivery lifecycle arrives in milestone 07.
/// </summary>
[Privacy(DataClassification.PrivateSensitive, "Kept while in use; discarded drafts are purged 30 days after discard; erased by the deletion workflow.")]
public sealed class DraftMessage : Entity
{
    public const int MaxBodyLength = 4_000;

    private DraftMessage()
    {
    }

    public Guid OwnerUserId { get; private set; }

    public Guid WorkspaceId { get; private set; }

    public string Body { get; private set; } = string.Empty;

    public DraftStatus Status { get; private set; }

    public DateTimeOffset? DiscardedAt { get; private set; }

    public string Fingerprint => ContentFingerprint.Of(Body);

    public static Result<DraftMessage> Create(Guid ownerUserId, Guid workspaceId, string? body, DateTimeOffset now)
    {
        if (ownerUserId == Guid.Empty || workspaceId == Guid.Empty)
        {
            return DomainError.Validation;
        }

        var draft = new DraftMessage { OwnerUserId = ownerUserId, WorkspaceId = workspaceId, Status = DraftStatus.Draft };
        var edited = draft.Edit(body, now);
        return edited.Succeeded ? Result.Ok(draft) : edited.Error!.Value;
    }

    public Result Edit(string? body, DateTimeOffset now)
    {
        if (Status != DraftStatus.Draft)
        {
            return Result.Fail(DomainError.Conflict);
        }

        var clean = body?.Trim() ?? string.Empty;
        if (clean.Length is 0 or > MaxBodyLength)
        {
            return Result.Fail(DomainError.Validation);
        }

        Body = clean;
        Touch(now);
        return Result.Ok();
    }

    public Result Discard(DateTimeOffset now)
    {
        if (Status != DraftStatus.Draft)
        {
            return Result.Fail(DomainError.Conflict);
        }

        Status = DraftStatus.Discarded;
        DiscardedAt = now;
        Touch(now);
        return Result.Ok();
    }

    internal void MarkShared(DateTimeOffset now)
    {
        Status = DraftStatus.Shared;
        Touch(now);
    }

    /// <summary>
    /// Shares this draft with a recipient. Requires the fingerprint of the exact text the author
    /// previewed and approved, so a draft edited after preview cannot be sent by mistake.
    /// </summary>
    public Result<SharedMessage> Share(Guid recipientUserId, string approvedFingerprint, Guid consentRecordId, DateTimeOffset now)
    {
        if (Status != DraftStatus.Draft)
        {
            return DomainError.Conflict;
        }

        if (!string.Equals(approvedFingerprint, Fingerprint, StringComparison.Ordinal))
        {
            return DomainError.ConsentMismatch;
        }

        if (recipientUserId == Guid.Empty || recipientUserId == OwnerUserId)
        {
            return DomainError.Validation;
        }

        MarkShared(now);
        return Result.Ok(SharedMessage.Create(this, recipientUserId, consentRecordId, now));
    }
}

public enum DraftStatus
{
    Draft = 1,
    Shared = 2,
    Discarded = 3,
}
