namespace Couples.Application.Notifications;

/// <summary>
/// In-app notifications. Each carries a kind and a message id only; the wording shown is fixed
/// per kind and never includes message text. Notifications held by quiet hours are not returned
/// until the quiet hours end.
/// </summary>
public interface INotificationService
{
    Task<int> UnreadCountAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationView>> ListAsync(Guid userId, CancellationToken cancellationToken = default);

    Task MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default);
}

public sealed record NotificationView(Guid Id, string Kind, string Text, Guid SubjectId, DateTimeOffset At, bool Read);
