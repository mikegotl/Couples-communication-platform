using Couples.Application.Abstractions;
using Couples.Application.Messaging;
using Couples.Domain.Auditing;
using Couples.Domain.Common;
using Couples.Domain.Consent;
using Couples.Domain.Messaging;
using Couples.Domain.Notifications;
using Couples.Domain.Pace;
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

        if (await BlocksJointFlowsAsync(userId, cancellationToken))
        {
            return DomainError.SafetyHold;
        }

        if (await OverShareLimitAsync(userId, cancellationToken))
        {
            return DomainError.RateLimited;
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

        if (await RecipientPrefsAsync(recipient.Value, cancellationToken) is { } prefs && prefs.IsPaused(clock.UtcNow))
        {
            return DomainError.PartnerPaused;
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

        // Sharing stops while the author's own safety check or safety mode says so.
        if (await BlocksJointFlowsAsync(userId, cancellationToken))
        {
            return DomainError.SafetyHold;
        }

        if (await OverShareLimitAsync(userId, cancellationToken))
        {
            return DomainError.RateLimited;
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
        var recipientPrefs = await RecipientPrefsAsync(recipient.Value, cancellationToken);
        if (recipientPrefs is not null && recipientPrefs.IsPaused(now))
        {
            // The partner asked for a break. The draft is kept; nothing is delivered or recorded.
            return DomainError.PartnerPaused;
        }

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

        // Metadata-only notice for the recipient, unless they muted notifications; held until
        // their quiet hours end. The message itself arrives either way.
        // A recipient in safety mode (or whose check suggests they may be unsafe) gets no notice,
        // so nothing appears on a device someone else may see. The message still arrives so the
        // sender's experience doesn't change and reveals nothing.
        if (recipientPrefs is not { Muted: true } && !await BlocksJointFlowsAsync(recipient.Value, cancellationToken))
        {
            var kind = draft.InReplyToMessageId is null ? NotificationKind.NewMessage : NotificationKind.Reply;
            var visibleAt = recipientPrefs?.NotificationVisibleAt(now) ?? now;
            db.Notifications.Add(Notification.Create(recipient.Value, kind, shared.Value.Id, visibleAt, now));
        }

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
        await db.Notifications.Where(n => n.SubjectId == message.Id).ExecuteDeleteAsync(cancellationToken);

        var saved = await SaveAsync(cancellationToken);
        if (saved.Succeeded)
        {
            LogRevoked(logger, message.Id);
        }

        return saved;
    }

    private async Task<bool> OverShareLimitAsync(Guid userId, CancellationToken cancellationToken)
    {
        var since = clock.UtcNow.AddHours(-1);
        return await db.SharedMessages.CountAsync(m => m.SenderUserId == userId && m.SharedAt > since, cancellationToken) >= MessagingLimits.SharesPerHour;
    }

    private Task<bool> BlocksJointFlowsAsync(Guid userId, CancellationToken cancellationToken) =>
        db.PersonalSafety.AnyAsync(s => s.UserId == userId && (s.SafetyMode || s.Level == Couples.Domain.Safety.SafetyLevel.Unsafe), cancellationToken);

    private Task<PacePreferences?> RecipientPrefsAsync(Guid recipientId, CancellationToken cancellationToken) =>
        db.PacePreferences.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == recipientId, cancellationToken);

    private IQueryable<DraftMessage> OwnedDrafts(Guid userId) => db.DraftMessages.Where(d => d.OwnerUserId == userId);

    private IQueryable<SharedMessage> ReadableByRecipient(Guid userId) => MessageAccess.ReadableByRecipient(db, userId);

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

    private static DraftView ToView(DraftMessage d) => new(d.Id, d.Body, d.Status.ToString(), d.Fingerprint, d.UpdatedAt, d.InReplyToMessageId);

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
