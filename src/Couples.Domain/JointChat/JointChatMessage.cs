using Couples.Domain.Common;
using Couples.Domain.Privacy;

namespace Couples.Domain.JointChat;

/// <summary>
/// A message in a joint chat. Both partners agreed to the chat, so both can read it while the
/// chat runs and for a short time after it ends; then it is deleted.
/// </summary>
[Privacy(DataClassification.SharedSensitive, "Readable by both partners while the chat runs and for 7 days after it ends, then deleted with the session; deleted at once if either partner deletes their account.")]
public sealed class JointChatMessage : Entity
{
    private JointChatMessage()
    {
    }

    public Guid SessionId { get; private set; }

    public Guid AuthorUserId { get; private set; }

    public string Body { get; private set; } = string.Empty;

    public DateTimeOffset SentAt { get; private set; }

    public static Result<JointChatMessage> Create(Guid sessionId, Guid authorUserId, string? body, DateTimeOffset now)
    {
        var text = body?.Trim() ?? string.Empty;
        if (text.Length is 0 or > JointChatPolicy.MaxMessageLength)
        {
            return DomainError.Validation;
        }

        var message = new JointChatMessage { SessionId = sessionId, AuthorUserId = authorUserId, Body = text, SentAt = now };
        message.Touch(now);
        return Result.Ok(message);
    }
}
