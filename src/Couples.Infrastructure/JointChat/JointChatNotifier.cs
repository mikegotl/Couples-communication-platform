using Couples.Application.JointChat;

namespace Couples.Infrastructure.JointChat;

/// <summary>In-process change signal (session id only) so both partners' open pages refresh.</summary>
internal sealed class JointChatNotifier : IJointChatNotifier
{
    public event Action<Guid>? SessionChanged;

    public void Notify(Guid sessionId) => SessionChanged?.Invoke(sessionId);
}
