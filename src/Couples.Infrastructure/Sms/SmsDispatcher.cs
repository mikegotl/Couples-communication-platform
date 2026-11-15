using Couples.Application.Abstractions;
using Couples.Application.Safety;
using Couples.Application.Sms;
using Couples.Domain.Notifications;
using Couples.Domain.Sms;
using Couples.Infrastructure.Messaging;
using Couples.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Couples.Infrastructure.Sms;

/// <summary>
/// Texts in-app notifications once they are due. Built on notifications on purpose: they already
/// follow every pace and safety rule (none while muted or paused, none for a user whose safety
/// settings hold joint flows, held until quiet hours end, removed when a message is withdrawn), so
/// texts follow the same rules without repeating them. Each notification is claimed in the
/// database before it is sent, so it is texted at most once.
/// </summary>
internal sealed partial class SmsDispatcher(
    CouplesDbContext db,
    IClock clock,
    SmsAvailability availability,
    ISmsSender sender,
    ISafetyGate safety,
    ILogger<SmsDispatcher> logger) : ISmsDispatcher
{
    /// <summary>Notifications older than this are left in the app rather than texted late.</summary>
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(36);

    public async Task<int> DispatchDueAsync(CancellationToken cancellationToken = default)
    {
        if (!await availability.IsAvailableAsync(cancellationToken))
        {
            return 0;
        }

        var now = clock.UtcNow;
        var oldest = now - MaxAge;
        var due = await (
                from n in db.Notifications.AsNoTracking()
                join s in db.SmsSettings.AsNoTracking() on n.RecipientUserId equals s.UserId
                where n.VisibleAt <= now && n.CreatedAt > oldest && n.ReadAt == null && n.CreatedAt >= s.ReceivingSince
                    && s.VerifiedAt != null && s.OptedOutAt == null
                    && !db.SmsDeliveries.Any(d => d.NotificationId == n.Id)
                orderby n.VisibleAt
                select new { n.Id, n.RecipientUserId, n.Kind, n.SubjectId, s.PhoneE164, s.IncludeMessageText })
            .Take(50)
            .ToListAsync(cancellationToken);

        var sent = 0;
        foreach (var item in due)
        {
            // Checked again at send time: safety mode may have been switched on since the notice was made.
            if (await safety.UserBlocksJointFlowsAsync(item.RecipientUserId, cancellationToken) || await OverDailyCapAsync(item.RecipientUserId, now, cancellationToken))
            {
                await TryAddAsync(SmsDelivery.Skip(item.Id, item.RecipientUserId, now), cancellationToken);
                continue;
            }

            var delivery = SmsDelivery.Claim(item.Id, item.RecipientUserId, now);
            if (!await TryAddAsync(delivery, cancellationToken))
            {
                continue; // Another instance claimed it.
            }

            var isReply = item.Kind == NotificationKind.Reply;
            var messagesUrl = availability.Link("/messages");
            string text;
            if (item.IncludeMessageText)
            {
                var body = await MessageAccess.ReadableByRecipient(db, item.RecipientUserId).AsNoTracking()
                    .Where(m => m.Id == item.SubjectId)
                    .Select(m => m.Body)
                    .FirstOrDefaultAsync(cancellationToken);
                if (body is null)
                {
                    // Withdrawn or no longer readable between the query and now: send nothing.
                    delivery.Failed(now);
                    await db.SaveChangesAsync(cancellationToken);
                    continue;
                }

                text = SmsText.WithMessage(isReply, body, availability.Options.MaxMessageCharacters, messagesUrl);
            }
            else
            {
                text = SmsText.Notice(isReply, messagesUrl);
            }

            SmsSendResult result;
            try
            {
                result = await sender.SendAsync(item.PhoneE164, text, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                LogSendFailed(logger, item.Id, ex.GetType().Name);
                result = new SmsSendResult(false);
            }

            var at = clock.UtcNow;
            if (result.Accepted)
            {
                delivery.Sent(result.ProviderMessageId, at);
                sent++;
            }
            else
            {
                delivery.Failed(at);
            }

            if (result.RecipientOptedOut)
            {
                var settings = await db.SmsSettings.FirstAsync(s => s.UserId == item.RecipientUserId, cancellationToken);
                settings.OptOut(at);
            }

            await db.SaveChangesAsync(cancellationToken);
            LogDispatched(logger, item.Id, result.Accepted);
        }

        return sent;
    }

    private async Task<bool> OverDailyCapAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var since = now.AddHours(-24);
        var count = await db.SmsDeliveries.CountAsync(
            d => d.RecipientUserId == userId && d.CreatedAt > since && (d.Status == SmsDeliveryStatus.Sent || d.Status == SmsDeliveryStatus.Sending),
            cancellationToken);
        return count >= availability.Options.DailyTextsPerUser;
    }

    private async Task<bool> TryAddAsync(SmsDelivery delivery, CancellationToken cancellationToken)
    {
        db.SmsDeliveries.Add(delivery);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(delivery).State = EntityState.Detached;
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Text for notification {NotificationId}; provider accepted: {Accepted}")]
    private static partial void LogDispatched(ILogger logger, Guid notificationId, bool accepted);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Text for notification {NotificationId} failed: {ErrorType}")]
    private static partial void LogSendFailed(ILogger logger, Guid notificationId, string errorType);
}
