using System.Text.RegularExpressions;

namespace Couples.Domain.Rewards;

/// <summary>
/// A small, transparent check for warm or appreciative wording in a shared message ("thank you",
/// "I appreciate", "sorry", "I hear you"). Runs in memory only; the text and the result for any
/// single message are never stored, logged or shown, only a weekly share across both partners.
/// Deliberately simple and kind: it can only add to a score, never flag a message as negative.
/// </summary>
public static partial class WarmLanguage
{
    /// <summary>Phrases counted as warm. Matched as whole words, case-insensitively.</summary>
    public static IReadOnlyList<string> Phrases { get; } =
    [
        "thank you", "thanks", "thankful", "grateful", "appreciate", "appreciated", "appreciation",
        "love you", "love that", "i love", "proud of you", "glad", "happy", "enjoyed", "lovely",
        "miss you", "missed you", "thinking of you", "sorry", "i apologise", "i apologize",
        "i understand", "i hear you", "that makes sense", "means a lot", "meant a lot",
        "looking forward", "you're right", "good point", "well done", "you matter", "i care",
        "hug", "kind of you", "so kind", "support you", "here for you", "we've got this", "together",
    ];

    private static readonly Regex PhrasePattern = new(
        @"(?<![\p{L}'])(" + string.Join('|', Phrases.Select(Regex.Escape)) + @")(?![\p{L}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(200));

    public static bool IsWarm(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.Replace('’', '\'');
        foreach (Match match in PhrasePattern.Matches(normalized))
        {
            if (!NegatedBefore(normalized, match.Index))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>"not happy", "never glad", "don't appreciate": the phrase doesn't count.</summary>
    private static bool NegatedBefore(string text, int index)
    {
        var start = Math.Max(0, index - 12);
        return NegationPattern().IsMatch(text[start..index]);
    }

    [GeneratedRegex(@"\b(not|never|don'?t|didn'?t|isn'?t|wasn'?t|no)\s+(really\s+|so\s+|very\s+)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex NegationPattern();
}
