namespace Couples.Domain.Safety;

/// <summary>
/// Phrases that suggest someone may be at risk (self-harm, threats, violence, fear). A
/// conservative, local check: a match never diagnoses anything, it only stops joint flows and
/// points the writer to support. Wording is part of the clinical review (docs/safety).
/// </summary>
public static class RiskSignals
{
    private static readonly string[] Phrases =
    [
        "kill myself", "killing myself", "end my life", "ending my life", "suicide", "suicidal", "want to die",
        "hurt myself", "harm myself", "self-harm", "self harm", "cut myself",
        "kill you", "kill me", "going to kill", "threatened to kill", "hits me", "hit me", "beats me", "choked me", "strangled",
        "afraid for my life", "scared for my life", "fear for my safety", "afraid to go home", "hurt the kids", "hurt the children",
    ];

    public static bool Matches(params string?[] texts) =>
        texts.Any(t => t is not null && Phrases.Any(s => t.Contains(s, StringComparison.OrdinalIgnoreCase)));
}
