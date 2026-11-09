using Couples.Application.Abstractions;
using Couples.Application.Notifications;
using Couples.Domain.Notifications;
using Couples.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Couples.Infrastructure.Pace;

internal sealed class NotificationService(CouplesDbContext db, IClock clock) : INotificationService
{
    /// <summary>Fixed wording per kind. Never built from message content.</summary>
    public static string TextFor(NotificationKind kind) => kind switch
    {
        NotificationKind.Reply => "Your partner replied to a message you shared.",
        _ => "Your partner shared a message with you.",
    };

    public Task<int> UnreadCountAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        return db.Notifications.CountAsync(n => n.RecipientUserId == userId && n.ReadAt == null && n.VisibleAt <= now, cancellationToken);
    }

    public async Task<IReadOnlyList<NotificationView>> ListAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var rows = await db.Notifications.AsNoTracking()
            .Where(n => n.RecipientUserId == userId && n.VisibleAt <= now)
            .OrderByDescending(n => n.VisibleAt)
            .Take(50)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(n => new NotificationView(n.Id, n.Kind.ToString(), TextFor(n.Kind), n.SubjectId, n.VisibleAt, n.ReadAt is not null))];
    }

    public async Task MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        await db.Notifications
            .Where(n => n.RecipientUserId == userId && n.ReadAt == null && n.VisibleAt <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now).SetProperty(n => n.UpdatedAt, now), cancellationToken);
    }
}
