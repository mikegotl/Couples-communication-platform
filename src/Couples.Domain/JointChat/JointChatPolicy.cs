namespace Couples.Domain.JointChat;

/// <summary>
/// The pilot's rules for a guided joint chat. Every value here is part of the clinical review
/// (docs/safety/joint-chat-review.md); changing one means bumping <see cref="Version"/>, which
/// changes what both partners consent to.
/// </summary>
public static class JointChatPolicy
{
    /// <summary>Identifies the rules and wording each partner agrees to before a chat.</summary>
    public const string Version = "joint-chat-2026-11-draft";

    public const int MaxMessageLength = 1_000;

    /// <summary>An invitation not answered within this time lapses.</summary>
    public static readonly TimeSpan InvitationExpiry = TimeSpan.FromMinutes(30);

    /// <summary>
    /// If the person whose turn it is hasn't replied within this time, either partner may write.
    /// Nobody is hurried; this only stops a chat from stalling.
    /// </summary>
    public static readonly TimeSpan TurnTimeout = TimeSpan.FromMinutes(5);

    /// <summary>A chat with no messages or pauses for this long ends.</summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(20);

    /// <summary>Longest a single chat can run, so it stays a short, guided conversation.</summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(60);

    /// <summary>Pause lengths offered for a cooling-off break.</summary>
    public static IReadOnlyList<int> PauseMinutes { get; } = [5, 10, 20];

    /// <summary>How long the transcript of an ended chat stays readable by both partners before it is deleted.</summary>
    public static readonly TimeSpan TranscriptKeptAfterEnd = TimeSpan.FromDays(7);

    /// <summary>Words that read as contempt or name-calling. A message containing one is held so the writer can rephrase.</summary>
    public static IReadOnlyList<string> HeldWords { get; } =
    [
        "stupid", "idiot", "pathetic", "worthless", "useless", "disgusting", "loser", "moron", "shut up",
        "hate you", "you always", "you never", "crazy", "psycho",
    ];
}
