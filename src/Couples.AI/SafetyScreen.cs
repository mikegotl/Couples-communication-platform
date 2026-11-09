namespace Couples.AI;

/// <summary>
/// A local, conservative first check run before anything is sent to a provider. If the text
/// suggests someone may be at risk, the coach shows support information instead of rewording,
/// and the text is not sent. The phrases are shared with joint chat moderation.
/// </summary>
internal static class SafetyScreen
{
    public static bool NeedsSupport(params string?[] texts) => Couples.Domain.Safety.RiskSignals.Matches(texts);
}
