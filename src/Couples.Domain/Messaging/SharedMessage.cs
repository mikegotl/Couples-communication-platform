using Couples.Domain.Common;
using Couples.Domain.Privacy;

namespace Couples.Domain.Messaging;

/// <summary>
/// Text a user explicitly approved for their partner. Readable by the sender and the recipient
/// only while both are active workspace members; the sender can revoke it.
/// </summary>
[Privacy(DataClassification.SharedSensitive, "Kept while the workspace exists; body erased when the sender revokes it or the sender's data is deleted.")]
public sealed class SharedMessage : Entity
{
    private SharedMessage()
    {
    }

    public Guid WorkspaceId { get; private set; }

    public Guid SenderUserId { get; private set; }

    public Guid RecipientUserId { get; private set; }

    public Guid? SourceDraftId { get; private set; }

    public Guid ConsentRecordId { get; private set; }

    public Guid? InReplyToMessageId { get; private set; }

    /// <summary>
    /// When the recipient first opened it. For the recipient's own "new" marker only; never shown
    /// to the sender, so there is no read receipt.
    /// </summary>
    public DateTimeOffset? RecipientOpenedAt { get; private set; }

    public string Body { get; private set; } = string.Empty;

    public DateTimeOffset SharedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsRevoked => RevokedAt is not null;

    internal static SharedMessage Create(DraftMessage draft, Guid recipientUserId, Guid consentRecordId, DateTimeOffset now)
    {
        var message = new SharedMessage
        {
            WorkspaceId = draft.WorkspaceId,
            SenderUserId = draft.OwnerUserId,
            RecipientUserId = recipientUserId,
            SourceDraftId = draft.Id,
            InReplyToMessageId = draft.InReplyToMessageId,
            ConsentRecordId = consentRecordId,
            Body = draft.Body,
            SharedAt = now,
        };
        message.Touch(now);
        return message;
    }

    public void MarkOpenedByRecipient(Guid actingUserId, DateTimeOffset now)
    {
        if (actingUserId == RecipientUserId && RecipientOpenedAt is null)
        {
            RecipientOpenedAt = now;
        }
    }

    /// <summary>
    /// Withdraws the message: the recipient can no longer read it and the text is erased from
    /// storage. A copy the recipient already read or wrote down cannot be recalled.
    /// </summary>
    public Result Revoke(Guid actingUserId, DateTimeOffset now)
    {
        if (actingUserId != SenderUserId)
        {
            return Result.Fail(DomainError.NotFound);
        }

        if (IsRevoked)
        {
            return Result.Ok();
        }

        RevokedAt = now;
        Body = string.Empty;
        Touch(now);
        return Result.Ok();
    }
}
