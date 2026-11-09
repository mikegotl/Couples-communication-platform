namespace Couples.AI;

/// <summary>
/// A local, conservative first check run before anything is sent to a provider. If the text
/// suggests someone may be at risk, the coach shows support information instead of rewording,
/// and the text is not sent. Milestone 09 replaces this with the full safety pathway.
/// </summary>
internal static class SafetyScreen
{
    private static readonly string[] Signals =
    [
        "kill myself", "killing myself", "end my life", "ending my life", "suicide", "suicidal", "want to die",
        "hurt myself", "harm myself", "self-harm", "self harm", "cut myself",
        "kill you", "kill me", "going to kill", "threatened to kill", "hits me", "hit me", "beats me", "choked me", "strangled",
        "afraid for my life", "scared for my life", "fear for my safety", "afraid to go home", "hurt the kids", "hurt the children",
    ];

    public static bool NeedsSupport(params string?[] texts) =>
        texts.Any(t => t is not null && Signals.Any(s => t.Contains(s, StringComparison.OrdinalIgnoreCase)));
}
