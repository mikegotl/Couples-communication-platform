using Couples.Domain.Common;
using Couples.Domain.JointChat;

namespace Couples.Application.JointChat;

/// <summary>
/// Independent sign-offs the joint chat pilot needs before anyone can use it, even with the
/// <see cref="FeatureFlags.Phase2JointChat"/> flag on. Each is a short reference to the signed
/// review (for example a document id), set in configuration by the owner.
/// </summary>
public sealed class JointChatOptions
{
    public const string SectionName = "JointChat";

    public string? ClinicalSignOff { get; set; }

    public string? SecuritySignOff { get; set; }

    public bool SignedOff => !string.IsNullOrWhiteSpace(ClinicalSignOff) && !string.IsNullOrWhiteSpace(SecuritySignOff);
}

public enum JointChatState
{
    /// <summary>Not offered to this workspace (or the pilot is off). The page explains nothing more.</summary>
    Unavailable = 1,

    /// <summary>The user's own safety check-in or safety mode keeps joint chat off. Only they see this.</summary>
    HeldForYou = 2,

    /// <summary>No chat is running; the user can start one.</summary>
    Ready = 3,

    WaitingForPartner = 4,
    InvitedByPartner = 5,
    Active = 6,
    Ended = 7,
}

/// <summary>What the person sees about how the last chat ended. Never says why beyond their own action.</summary>
public enum JointChatEnding
{
    YouLeft = 1,
    YouDeclined = 2,

    /// <summary>Your invitation lapsed.</summary>
    NotJoined = 3,

    /// <summary>Anything else, including your partner leaving, a time limit, moderation or a safety hold.</summary>
    Ended = 4,
}

public sealed record JointChatLine(Guid Id, bool Mine, string Body, DateTimeOffset SentAt);

public sealed record JointChatView(
    JointChatState State,
    Guid? SessionId,
    bool MyTurn,
    bool OpenTurn,
    bool Paused,
    bool PausedByMe,
    DateTimeOffset? PausedUntil,
    DateTimeOffset? InvitationExpiresAt,
    DateTimeOffset? EndsBy,
    JointChatEnding? Ending,
    DateTimeOffset? TranscriptDeletedAt,
    IReadOnlyList<JointChatLine> Transcript)
{
    public static JointChatView Of(JointChatState state) => new(state, null, false, false, false, false, null, null, null, null, null, []);
}

public sealed record JointChatSendResult(ModerationOutcome Outcome, JointChatView View);

/// <summary>
/// The Phase 2 joint chat pilot (ADR-0015). Every method first checks the
/// <see cref="FeatureFlags.Phase2JointChat"/> flag, both sign-offs, the workspace's eligibility,
/// membership and the safety gate; when any fails the caller gets
/// <see cref="JointChatState.Unavailable"/> or <see cref="DomainError.NotFound"/> and nothing is
/// written.
/// </summary>
public interface IJointChatService
{
    /// <summary>Cheap check for navigation: is joint chat offered to this user's workspace right now?</summary>
    Task<bool> IsOfferedAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<JointChatView> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Starts a chat and invites the partner. <paramref name="policyVersion"/> is the version the user agreed to.</summary>
    Task<Result<JointChatView>> StartAsync(Guid userId, string policyVersion, CancellationToken cancellationToken = default);

    Task<Result<JointChatView>> JoinAsync(Guid userId, Guid sessionId, string policyVersion, CancellationToken cancellationToken = default);

    Task<Result<JointChatView>> DeclineAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>Always succeeds for a participant while the chat is running, whatever else is true.</summary>
    Task<Result<JointChatView>> LeaveAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default);

    Task<Result<JointChatView>> PauseAsync(Guid userId, Guid sessionId, int minutes, CancellationToken cancellationToken = default);

    Task<Result<JointChatView>> ResumeAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default);

    Task<Result<JointChatSendResult>> SendAsync(Guid userId, Guid sessionId, string? text, CancellationToken cancellationToken = default);
}

public sealed record EligibleWorkspace(Guid WorkspaceId, string Reference, DateTimeOffset GrantedAt, bool ChatRunning);

public sealed record JointChatAdminOverview(bool FlagOn, string? ClinicalSignOff, string? SecuritySignOff, IReadOnlyList<EligibleWorkspace> Workspaces);

/// <summary>Per-workspace eligibility, for administrators only (checked against the Admin role here, not only in the UI).</summary>
public interface IJointChatAdminService
{
    Task<Result<JointChatAdminOverview>> GetOverviewAsync(Guid adminUserId, CancellationToken cancellationToken = default);

    Task<Result> GrantAsync(Guid adminUserId, Guid workspaceId, string? reference, CancellationToken cancellationToken = default);

    /// <summary>Revoking ends any running chat in that workspace.</summary>
    Task<Result> RevokeAsync(Guid adminUserId, Guid workspaceId, CancellationToken cancellationToken = default);
}

/// <summary>Lets open chat pages refresh when the other partner acts. In-process (one web replica).</summary>
public interface IJointChatNotifier
{
    event Action<Guid>? SessionChanged;

    void Notify(Guid sessionId);
}
