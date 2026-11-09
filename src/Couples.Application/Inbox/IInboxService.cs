using Couples.Domain.Common;

namespace Couples.Application.Inbox;

/// <summary>
/// The recipient's side of an exchange: what arrived, a private place to think about it, and an
/// optional reply. Opening a message is recorded for the recipient's own "new" marker only and is
/// never shown to the sender. Nothing here pushes the recipient to reply.
/// </summary>
public interface IInboxService
{
    Task<IReadOnlyList<InboxItem>> ListAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Opens a received message (marking it seen for the recipient only) with the recipient's own reflection and reply draft.</summary>
    Task<Result<ReceivedMessage>> OpenAsync(Guid userId, Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>Creates or updates the user's private reflection on a message they received. Empty text deletes it.</summary>
    Task<Result> SaveReflectionAsync(Guid userId, Guid messageId, string? body, CancellationToken cancellationToken = default);

    /// <summary>Starts a private reply draft, or returns the one already in progress for this message.</summary>
    Task<Result<Guid>> StartReplyAsync(Guid userId, Guid messageId, string? body, CancellationToken cancellationToken = default);
}

public sealed record InboxItem(Guid MessageId, string Preview, DateTimeOffset SharedAt, bool IsNew, bool IsReplyToMe, bool HasMyReflection, Guid? MyReplyDraftId, bool IReplied);

public sealed record ReceivedMessage(
    Guid MessageId,
    string Body,
    DateTimeOffset SharedAt,
    string? MyEarlierMessage,
    string? MyReflection,
    Guid? MyReplyDraftId,
    bool IReplied);
