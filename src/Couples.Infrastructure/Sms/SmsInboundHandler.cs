using Couples.Application.Abstractions;
using Couples.Application.Messaging;
using Couples.Application.Safety;
using Couples.Application.Sms;
using Couples.Domain.Auditing;
using Couples.Domain.Common;
using Couples.Domain.Messaging;
using Couples.Infrastructure.Messaging;
using Couples.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Couples.Infrastructure.Sms;

/// <summary>
/// A text from a user. A reply never goes straight to the partner: it becomes the user's private
/// draft, its exact text is echoed back, and only a SEND reply shares it, through the same
/// <see cref="IMessagingService.ShareAsync"/> path (fingerprint, consent record, pace and safety
/// checks) as the app. STOP, START and HELP follow carrier conventions.
/// </summary>
internal sealed partial class SmsInboundHandler(
    CouplesDbContext db,
    IClock clock,
    SmsAvailability availability,
    IMessagingService messaging,
    ISafetyGate safety,
    ILogger<SmsInboundHandler> logger) : ISmsInbound
{
    /// <summary>Most replies turned into drafts per person per hour, so a stuck phone can't flood anything.</summary>
    private const int MaxDraftsPerHour = 20;

    public async Task<string?> HandleAsync(string from, string? body, CancellationToken cancellationToken = default)
    {
        if (!await availability.IsAvailableAsync(cancellationToken)
            || !PhoneNumber.TryNormalize(from, availability.Options.DefaultCallingCode, out var phone))
        {
            return null;
        }

        var settings = await db.SmsSettings.FirstOrDefaultAsync(s => s.PhoneE164 == phone && s.VerifiedAt != null, cancellationToken);
        var text = (body ?? string.Empty).Trim();
        var keyword = text.Length <= 12 ? text.TrimEnd('.', '!').ToUpperInvariant() : string.Empty;
        if (settings is null)
        {
            // Unknown numbers get one neutral line (never for STOP, which the provider answers).
            return SmsText.StopWords.Contains(keyword) || text.Length == 0 ? null : SmsText.NotSetUp;
        }

        var userId = settings.UserId;
        var now = clock.UtcNow;

        if (SmsText.StopWords.Contains(keyword))
        {
            settings.OptOut(now);
            db.AuditEvents.Add(AuditEvent.Record(userId, AuditActions.SmsStopped, null, null, now));
            await db.SaveChangesAsync(cancellationToken);
            LogInbound(logger, userId, "stop");
            return null; // The provider sends the carrier-required confirmation.
        }

        if (SmsText.StartWords.Contains(keyword))
        {
            settings.OptIn(now);
            await db.SaveChangesAsync(cancellationToken);
            LogInbound(logger, userId, "start");
            return SmsText.Restarted;
        }

        if (settings.OptedOutAt is not null)
        {
            return null;
        }

        if (SmsText.HelpWords.Contains(keyword))
        {
            return SmsText.Help(availability.Link("/settings/texts"));
        }

        // While the user's own safety settings hold joint flows nothing is drafted or shared, and
        // the reply says nothing about why: someone else may be reading the phone.
        if (await safety.UserBlocksJointFlowsAsync(userId, cancellationToken))
        {
            settings.ClearPending();
            await db.SaveChangesAsync(cancellationToken);
            LogInbound(logger, userId, "held");
            return SmsText.Unavailable;
        }

        if (SmsText.SendWords.Contains(keyword))
        {
            return await ConfirmAsync(settings, now, cancellationToken);
        }

        if (text.Length == 0)
        {
            return null;
        }

        if (text.Length > DraftMessage.MaxBodyLength)
        {
            return SmsText.TooLong;
        }

        var since = now.AddHours(-1);
        if (await db.DraftMessages.CountAsync(d => d.OwnerUserId == userId && d.CreatedAt > since, cancellationToken) >= MaxDraftsPerHour)
        {
            return null;
        }

        return await DraftReplyAsync(settings, text, now, cancellationToken);
    }

    private async Task<string> DraftReplyAsync(Domain.Sms.SmsSettings settings, string text, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var userId = settings.UserId;

        // The draft answers the latest message the user can read from their partner, as the app's Reply does.
        var latest = await MessageAccess.ReadableByRecipient(db, userId).AsNoTracking()
            .OrderByDescending(m => m.SharedAt)
            .Select(m => new { m.Id, m.WorkspaceId })
            .FirstOrDefaultAsync(cancellationToken);
        var workspaceId = latest?.WorkspaceId ?? await db.WorkspaceMemberships
            .Where(m => m.UserId == userId && m.LeftAt == null)
            .Select(m => (Guid?)m.WorkspaceId)
            .FirstOrDefaultAsync(cancellationToken);
        if (workspaceId is null)
        {
            return SmsText.NoPartner;
        }

        var created = DraftMessage.Create(userId, workspaceId.Value, text, now, latest?.Id);
        if (!created.Succeeded)
        {
            return SmsText.TooLong;
        }

        db.DraftMessages.Add(created.Value);
        settings.ClearPending();
        await db.SaveChangesAsync(cancellationToken);

        var preview = await messaging.PreviewShareAsync(userId, created.Value.Id, cancellationToken);
        if (!preview.Succeeded)
        {
            LogInbound(logger, userId, "draft_kept");
            return await ExplainAsync(userId, preview.Error!.Value, cancellationToken);
        }

        settings.AwaitConfirmation(created.Value.Id, preview.Value.Fingerprint, now);
        await db.SaveChangesAsync(cancellationToken);
        LogInbound(logger, userId, "awaiting_send");
        return SmsText.ConfirmReply(preview.Value.Body);
    }

    private async Task<string> ConfirmAsync(Domain.Sms.SmsSettings settings, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (settings.PendingConfirmation(now) is not { } pending)
        {
            return SmsText.NothingWaiting;
        }

        settings.ClearPending();
        await db.SaveChangesAsync(cancellationToken);

        var shared = await messaging.ShareAsync(settings.UserId, pending.DraftId, pending.Fingerprint, cancellationToken);
        LogInbound(logger, settings.UserId, shared.Succeeded ? "shared" : "share_refused");
        return shared.Succeeded ? SmsText.Sent : await ExplainAsync(settings.UserId, shared.Error!.Value, cancellationToken);
    }

    private async Task<string> ExplainAsync(Guid userId, DomainError error, CancellationToken cancellationToken)
    {
        switch (error)
        {
            case DomainError.NoPartner:
                return SmsText.NoPartner;
            case DomainError.RateLimited:
                return SmsText.Slow;
            case DomainError.ConsentMismatch:
            case DomainError.Conflict:
                return SmsText.DraftChanged;
            case DomainError.SafetyHold:
                return SmsText.Unavailable;
            case DomainError.PartnerPaused:
                var until = await (
                        from me in db.WorkspaceMemberships
                        join other in db.WorkspaceMemberships on me.WorkspaceId equals other.WorkspaceId
                        join p in db.PacePreferences on other.UserId equals p.UserId
                        where me.UserId == userId && me.LeftAt == null && other.UserId != userId && other.LeftAt == null
                        select p.PausedUntil)
                    .FirstOrDefaultAsync(cancellationToken);
                return until is { } at ? SmsText.PartnerPaused(at) : SmsText.Failed;
            default:
                return SmsText.Failed;
        }
    }

    // Ids and an outcome word only: never the number or the text.
    [LoggerMessage(Level = LogLevel.Information, Message = "Text from user {UserId}: {Outcome}")]
    private static partial void LogInbound(ILogger logger, Guid userId, string outcome);
}
