using Couples.Domain.Safety;

namespace Couples.Domain.JointChat;

public enum ModerationOutcome
{
    /// <summary>Delivered to both partners.</summary>
    Allowed = 1,

    /// <summary>Not delivered; the writer is asked to rephrase. Nothing is stored.</summary>
    HeldToRephrase = 2,

    /// <summary>
    /// Not delivered; the chat ends and the writer, only, is shown support. The partner sees the
    /// same neutral "This chat has ended" as for any other ending.
    /// </summary>
    EndedForSafety = 3,
}

/// <summary>
/// Deterministic, local moderation run before a joint chat message is delivered. No text leaves
/// the app for this check and no held text is stored or logged.
/// </summary>
public static class JointChatModeration
{
    public static ModerationOutcome Check(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (RiskSignals.Matches(text))
        {
            return ModerationOutcome.EndedForSafety;
        }

        return JointChatPolicy.HeldWords.Any(w => ContainsPhrase(text, w)) ? ModerationOutcome.HeldToRephrase : ModerationOutcome.Allowed;
    }

    private static bool ContainsPhrase(string text, string phrase)
    {
        for (var i = text.IndexOf(phrase, StringComparison.OrdinalIgnoreCase); i >= 0; i = text.IndexOf(phrase, i + 1, StringComparison.OrdinalIgnoreCase))
        {
            var before = i == 0 || !char.IsLetter(text[i - 1]);
            var end = i + phrase.Length;
            var after = end >= text.Length || !char.IsLetter(text[end]);
            if (before && after)
            {
                return true;
            }
        }

        return false;
    }
}
