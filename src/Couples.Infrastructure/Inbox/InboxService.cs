using Couples.Application.Abstractions;
using Couples.Application.Inbox;
using Couples.Domain.Common;
using Couples.Domain.Messaging;
using Couples.Domain.Reflections;
using Couples.Infrastructure.Messaging;
using Couples.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Couples.Infrastructure.Inbox;

internal sealed class InboxService(CouplesDbContext db, IClock clock) : IInboxService
{
    private const int PreviewLength = 140;

    public async Task<IReadOnlyList<InboxItem>> ListAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var rows = await MessageAccess.ReadableByRecipient(db, userId).AsNoTracking()
            .OrderByDescending(m => m.SharedAt)
            .Select(m => new
            {
                m.Id,
                m.Body,
                m.SharedAt,
                m.RecipientOpenedAt,
                IsReply = m.InReplyToMessageId != null,
                Reflected = db.PrivateReflections.Any(r => r.OwnerUserId == userId && r.AboutMessageId == m.Id),
                Draft = db.DraftMessages.Where(d => d.OwnerUserId == userId && d.InReplyToMessageId == m.Id && d.Status == DraftStatus.Draft)
                    .Select(d => (Guid?)d.Id).FirstOrDefault(),
                Replied = db.SharedMessages.Any(r => r.SenderUserId == userId && r.InReplyToMessageId == m.Id && r.RevokedAt == null),
            })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(r => new InboxItem(r.Id, Preview(r.Body), r.SharedAt, r.RecipientOpenedAt is null, r.IsReply, r.Reflected, r.Draft, r.Replied))];
    }

    public async Task<Result<ReceivedMessage>> OpenAsync(Guid userId, Guid messageId, CancellationToken cancellationToken = default)
    {
        var message = await MessageAccess.ReadableByRecipient(db, userId).FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);
        if (message is null)
        {
            return DomainError.NotFound;
        }

        var now = clock.UtcNow;
        message.MarkOpenedByRecipient(userId, now);
        await db.Notifications
            .Where(n => n.RecipientUserId == userId && n.SubjectId == messageId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now).SetProperty(n => n.UpdatedAt, now), cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Opened at the same moment elsewhere; the marker is already set.
        }

        var earlier = message.InReplyToMessageId is { } original
            ? await db.SharedMessages.AsNoTracking()
                .Where(m => m.Id == original && m.SenderUserId == userId && m.RevokedAt == null)
                .Select(m => m.Body)
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        var reflection = await db.PrivateReflections.AsNoTracking()
            .Where(r => r.OwnerUserId == userId && r.AboutMessageId == messageId)
            .Select(r => r.Body)
            .FirstOrDefaultAsync(cancellationToken);
        var draft = await db.DraftMessages.AsNoTracking()
            .Where(d => d.OwnerUserId == userId && d.InReplyToMessageId == messageId && d.Status == DraftStatus.Draft)
            .Select(d => (Guid?)d.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var replied = await db.SharedMessages.AnyAsync(r => r.SenderUserId == userId && r.InReplyToMessageId == messageId && r.RevokedAt == null, cancellationToken);

        return Result.Ok(new ReceivedMessage(message.Id, message.Body, message.SharedAt, earlier, reflection, draft, replied));
    }

    public async Task<Result> SaveReflectionAsync(Guid userId, Guid messageId, string? body, CancellationToken cancellationToken = default)
    {
        if (!await MessageAccess.ReadableByRecipient(db, userId).AnyAsync(m => m.Id == messageId, cancellationToken))
        {
            return Result.Fail(DomainError.NotFound);
        }

        var existing = await db.PrivateReflections.FirstOrDefaultAsync(r => r.OwnerUserId == userId && r.AboutMessageId == messageId, cancellationToken);
        var now = clock.UtcNow;
        if (string.IsNullOrWhiteSpace(body))
        {
            if (existing is not null)
            {
                db.PrivateReflections.Remove(existing);
            }
        }
        else if (existing is null)
        {
            var created = PrivateReflection.AboutMessage(userId, messageId, body, now);
            if (!created.Succeeded)
            {
                return Result.Fail(created.Error!.Value);
            }

            db.PrivateReflections.Add(created.Value);
        }
        else
        {
            var updated = existing.Update(existing.Title, body, now);
            if (!updated.Succeeded)
            {
                return updated;
            }
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Result.Ok();
        }
        catch (DbUpdateException)
        {
            return Result.Fail(DomainError.Conflict);
        }
    }

    public async Task<Result<Guid>> StartReplyAsync(Guid userId, Guid messageId, string? body, CancellationToken cancellationToken = default)
    {
        var message = await MessageAccess.ReadableByRecipient(db, userId).AsNoTracking().FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);
        if (message is null)
        {
            return DomainError.NotFound;
        }

        var existing = await db.DraftMessages
            .Where(d => d.OwnerUserId == userId && d.InReplyToMessageId == messageId && d.Status == DraftStatus.Draft)
            .Select(d => (Guid?)d.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is { } id)
        {
            return Result.Ok(id);
        }

        var created = DraftMessage.Create(userId, message.WorkspaceId, body, clock.UtcNow, message.Id);
        if (!created.Succeeded)
        {
            return created.Error!.Value;
        }

        db.DraftMessages.Add(created.Value);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Ok(created.Value.Id);
    }

    private static string Preview(string body) => body.Length <= PreviewLength ? body : body[..PreviewLength].TrimEnd() + "…";
}
