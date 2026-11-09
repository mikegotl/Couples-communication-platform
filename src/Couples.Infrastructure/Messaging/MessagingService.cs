using Couples.Application.Abstractions;
using Couples.Application.Messaging;
using Couples.Domain.Auditing;
using Couples.Domain.Common;
using Couples.Domain.Consent;
using Couples.Domain.Messaging;
using Couples.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Couples.Infrastructure.Messaging;

internal sealed partial class MessagingService(CouplesDbContext db, IClock clock, ILogger<MessagingService> logger) : IMessagingService
{
    public async Task<Result<DraftView>> CreateDraftAsync(Guid userId, string? body, CancellationToken cancellationToken = default)
    {
        var workspaceId = await ActiveWorkspaceIdAsync(userId, cancellationToken);
        if (workspaceId is null)
        {
            return DomainError.NotFound;
        }

        var created = DraftMessage.Create(userId, workspaceId.Value, body, clock.UtcNow);
        if (!created.Succeeded)
        {
            return created.Error!.Value;
        }

        db.DraftMessages.Add(created.Value);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Ok(ToView(created.Value));
    }

    public async Task<Result<DraftView>> EditDraftAsync(Guid userId, Guid draftId, string? body, CancellationToken cancellationToken = default)
    {
        var draft = await OwnedDrafts(userId).FirstOrDefaultAsync(d => d.Id == draftId, cancellationToken);
        if (draft is null)
        {
            return DomainError.NotFound;
        }

        var edited = draft.Edit(body, clock.UtcNow);
        if (!edited.Succeeded)
        {
            return edited.Error!.Value;
        }

        var saved = await SaveAsync(cancellationToken);
        return saved.Succeeded ? Result.Ok(ToView(draft)) : saved.Error!.Value;
    }

    public async Task<Result<DraftView>> GetDraftAsync(Guid userId, Guid draftId, CancellationToken cancellationToken = default)
    {
        var draft = await OwnedDrafts(userId).AsNoTracking().FirstOrDefaultAsync(d => d.Id == draftId, cancellationToken);
        return draft is null ? DomainError.NotFound : Result.Ok(ToView(draft));
    }

    public async Task<IReadOnlyList<DraftView>> ListDraftsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var drafts = await OwnedDrafts(userId).AsNoTracking()
            .Where(d => d.Status == DraftStatus.Draft)
            .OrderByDescending(d => d.UpdatedAt)
            .ToListAsync(cancellationToken);
        return [.. drafts.Select(ToView)];
    }

    public async Task<Result> DiscardDraftAsync(Guid userId, Guid draftId, CancellationToken cancellationToken = default)
    {
        var draft = await OwnedDrafts(userId).FirstOrDefaultAsync(d => d.Id == draftId, cancellationToken);
        if (draft is null)
        {
            return Result.Fail(DomainError.NotFound);
        }

        var discarded = draft.Discard(clock.UtcNow);
        return discarded.Succeeded ? await SaveAsync(cancellationToken) : discarded;
    }

    public async Task<Result<SharePreview>> PreviewShareAsync(Guid userId, Guid draftId, CancellationToken cancellationToken = default)
    {
        var draft = await OwnedDrafts(userId).AsNoTracking().FirstOrDefaultAsync(d => d.Id == draftId, cancellationToken);
        if (draft is null)
        {
            return DomainError.NotFound;
        }

        if (draft.Status != DraftStatus.Draft)
        {
            return DomainError.Conflict;
        }

        var (isMember, recipient) = await RecipientAsync(userId, draft.WorkspaceId, cancellationToken);
        if (!isMember)
        {
            return DomainError.NotFound;
        }

        if (recipient is null)
        {
            return DomainError.NoPartner;
        }

        var name = await db.Workspaces.Where(w => w.Id == draft.WorkspaceId).Select(w => w.Name).FirstAsync(cancellationToken);
        return Result.Ok(new SharePreview(draft.Id, draft.Body, draft.Fingerprint, name));
    }

    public async Task<Result<SharedMessageView>> ShareAsync(Guid userId, Guid draftId, string approvedFingerprint, CancellationToken cancellationToken = default)
    {
        var draft = await OwnedDrafts(userId).FirstOrDefaultAsync(d => d.Id == draftId, cancellationToken);
        if (draft is null)
        {
            return DomainError.NotFound;
        }

        // A repeated confirmation (double click, retry after a lost response) returns the original
        // delivery instead of failing or sharing twice.
        if (draft.Status == DraftStatus.Shared)
        {
            return await ExistingDeliveryAsync(userId, draft.Id, approvedFingerprint, draft.Fingerprint, cancellationToken);
        }

        // The author must still be an active member of the draft's workspace, and a partner must be there to receive it.
        var (isMember, recipient) = await RecipientAsync(userId, draft.WorkspaceId, cancellationToken);
        if (!isMember)
        {
            return DomainError.NotFound;
        }

        if (recipient is null)
        {
            return DomainError.NoPartner;
        }

        var now = clock.UtcNow;
        var fingerprint = draft.Fingerprint;
        var consent = ConsentRecord.Grant(userId, draft.WorkspaceId, ConsentPurpose.ShareMessage, draft.Id, approvedFingerprint, now);
        var shared = draft.Share(recipient.Value, approvedFingerprint ?? string.Empty, consent.Id, now);
        if (!shared.Succeeded)
        {
            return shared.Error!.Value;
        }

        db.ConsentRecords.Add(consent);
        db.SharedMessages.Add(shared.Value);
        db.AuditEvents.Add(AuditEvent.Record(userId, AuditActions.MessageShared, draft.WorkspaceId, shared.Value.Id, now));

        var saved = await SaveAsync(cancellationToken);
        if (!saved.Succeeded)
        {
            // Lost a race with a concurrent confirmation of the same draft: nothing from this
            // request was written; return the delivery the other request made.
            db.ChangeTracker.Clear();
            return await ExistingDeliveryAsync(userId, draft.Id, approvedFingerprint, fingerprint, cancellationToken);
        }

        LogShared(logger, shared.Value.Id, draft.WorkspaceId);
        return Result.Ok(ToView(shared.Value, userId));
    }

    private async Task<Result<SharedMessageView>> ExistingDeliveryAsync(Guid userId, Guid draftId, string? approvedFingerprint, string draftFingerprint, CancellationToken cancellationToken)
    {
        var existing = await db.SharedMessages.AsNoTracking()
            .FirstOrDefaultAsync(m => m.SourceDraftId == draftId && m.SenderUserId == userId, cancellationToken);
        if (existing is null || !string.Equals(approvedFingerprint, draftFingerprint, StringComparison.Ordinal))
        {
            return DomainError.Conflict;
        }

        LogDuplicateShare(logger, existing.Id);
        return Result.Ok(ToView(existing, userId));
    }

    private async Task<(bool IsMember, Guid? Recipient)> RecipientAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken)
    {
        var members = await db.WorkspaceMemberships
            .Where(m => m.WorkspaceId == workspaceId && m.LeftAt == null)
            .Select(m => m.UserId)
            .ToListAsync(cancellationToken);
        return (members.Contains(userId), members.Where(m => m != userId).Cast<Guid?>().FirstOrDefault());
    }

    public async Task<IReadOnlyList<SharedMessageView>> InboxAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var messages = await ReadableByRecipient(userId).AsNoTracking().OrderByDescending(m => m.SharedAt).ToListAsync(cancellationToken);
        return [.. messages.Select(m => ToView(m, userId))];
    }

    public async Task<IReadOnlyList<SharedMessageView>> SentAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var messages = await db.SharedMessages.AsNoTracking()
            .Where(m => m.SenderUserId == userId)
            .OrderByDescending(m => m.SharedAt)
            .ToListAsync(cancellationToken);
        return [.. messages.Select(m => ToView(m, userId))];
    }

    public async Task<Result<SharedMessageView>> GetSharedAsync(Guid userId, Guid messageId, CancellationToken cancellationToken = default)
    {
        var message = await db.SharedMessages.AsNoTracking()
                .Where(m => m.Id == messageId && m.SenderUserId == userId)
                .FirstOrDefaultAsync(cancellationToken)
            ?? await ReadableByRecipient(userId).AsNoTracking().FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);

        return message is null ? DomainError.NotFound : Result.Ok(ToView(message, userId));
    }

    public async Task<Result> RevokeAsync(Guid userId, Guid messageId, CancellationToken cancellationToken = default)
    {
        var message = await db.SharedMessages.FirstOrDefaultAsync(m => m.Id == messageId && m.SenderUserId == userId, cancellationToken);
        if (message is null)
        {
            return Result.Fail(DomainError.NotFound);
        }

        if (message.IsRevoked)
        {
            return Result.Ok();
        }

        var now = clock.UtcNow;
        message.Revoke(userId, now);
        var consent = await db.ConsentRecords.FirstOrDefaultAsync(c => c.Id == message.ConsentRecordId && c.UserId == userId, cancellationToken);
        consent?.Withdraw(now);
        db.AuditEvents.Add(AuditEvent.Record(userId, AuditActions.MessageRevoked, message.WorkspaceId, message.Id, now));

        var saved = await SaveAsync(cancellationToken);
        if (saved.Succeeded)
        {
            LogRevoked(logger, message.Id);
        }

        return saved;
    }

    private IQueryable<DraftMessage> OwnedDrafts(Guid userId) => db.DraftMessages.Where(d => d.OwnerUserId == userId);

    /// <summary>
    /// What a recipient may read: addressed to them, not revoked, and both they and the sender
    /// are still active members of the workspace. Leaving (by either partner) ends access.
    /// </summary>
    private IQueryable<SharedMessage> ReadableByRecipient(Guid userId) =>
        db.SharedMessages.Where(m =>
            m.RecipientUserId == userId
            && m.RevokedAt == null
            && db.WorkspaceMemberships.Any(r => r.WorkspaceId == m.WorkspaceId && r.UserId == userId && r.LeftAt == null)
            && db.WorkspaceMemberships.Any(s => s.WorkspaceId == m.WorkspaceId && s.UserId == m.SenderUserId && s.LeftAt == null));

    private Task<Guid?> ActiveWorkspaceIdAsync(Guid userId, CancellationToken cancellationToken) =>
        db.WorkspaceMemberships
            .Where(m => m.UserId == userId && m.LeftAt == null)
            .Select(m => (Guid?)m.WorkspaceId)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<Result> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Result.Ok();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Fail(DomainError.Conflict);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Result.Fail(DomainError.Conflict);
        }
    }

    private static DraftView ToView(DraftMessage d) => new(d.Id, d.Body, d.Status.ToString(), d.Fingerprint, d.UpdatedAt);

    private static SharedMessageView ToView(SharedMessage m, Guid viewer) =>
        new(m.Id, m.SenderUserId == viewer, m.Body, m.SharedAt, m.IsRevoked);

    // Ids only. Message text never reaches logs.
    [LoggerMessage(Level = LogLevel.Information, Message = "Message {MessageId} shared in workspace {WorkspaceId}")]
    private static partial void LogShared(ILogger logger, Guid messageId, Guid workspaceId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Repeated share confirmation returned existing message {MessageId}")]
    private static partial void LogDuplicateShare(ILogger logger, Guid messageId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Message {MessageId} revoked by sender")]
    private static partial void LogRevoked(ILogger logger, Guid messageId);
}
