using System.Security.Cryptography;
using System.Text;

namespace Couples.Domain.Safety;

/// <summary>
/// The private safety check. PLACEHOLDER pending licensed clinician review: the questions,
/// answer options and the rule that turns answers into a level are original to this project and
/// are not a validated screening instrument, a risk assessment or a diagnosis. Change them only
/// as a new <see cref="Version"/>; a test pins the content hash so an edit is a visible decision.
/// </summary>
public static class SafetyContent
{
    public const int Version = 1;

    public const string ReviewStatus = "Pending licensed clinician review";

    public static readonly SafetyQuestion SafeAtHome = new(
        "safe-at-home",
        "Do you feel safe at home right now?",
        [new("yes", "Yes"), new("unsure", "I'm not sure"), new("no", "No")]);

    public static readonly SafetyQuestion AfraidOfPartner = new(
        "afraid",
        "Has your partner threatened you, hurt you, or made you afraid?",
        [new("no", "No"), new("sometimes", "Sometimes"), new("yes", "Yes"), new("skip", "I'd rather not say")]);

    public static readonly SafetyQuestion CanSayNo = new(
        "can-say-no",
        "Do you feel able to disagree with your partner or say no without worrying about what they'll do?",
        [new("yes", "Yes"), new("not-always", "Not always"), new("no", "No")]);

    public static readonly SafetyQuestion SelfHarm = new(
        "self-harm",
        "Are you having thoughts of hurting yourself or ending your life?",
        [new("no", "No"), new("yes", "Yes"), new("skip", "I'd rather not say")]);

    public static IReadOnlyList<SafetyQuestion> Questions { get; } = [SafeAtHome, AfraidOfPartner, CanSayNo, SelfHarm];

    /// <summary>SHA-256 of the questions, options and version, pinned by a test.</summary>
    public static string ContentHash
    {
        get
        {
            var text = new StringBuilder().Append(Version).Append('|');
            foreach (var q in Questions)
            {
                text.Append(q.Key).Append('=').Append(q.Text).Append('[');
                foreach (var o in q.Options)
                {
                    text.Append(o.Key).Append(':').Append(o.Label).Append(';');
                }

                text.Append(']');
            }

            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
        }
    }

    /// <summary>
    /// Placeholder rule (pending clinician review). Any sign of fear, harm or coercion is
    /// <see cref="SafetyLevel.Unsafe"/>; uncertainty or declining to answer the partner question
    /// is <see cref="SafetyLevel.Concern"/>. Self-harm is reported separately so crisis support
    /// is shown whatever the level.
    /// </summary>
    public static (SafetyLevel Level, bool SelfHarmSupport) Assess(IReadOnlyDictionary<string, string> answers)
    {
        ArgumentNullException.ThrowIfNull(answers);
        string? A(SafetyQuestion q) => answers.GetValueOrDefault(q.Key);

        var unsafeSigns = A(SafeAtHome) == "no" || A(AfraidOfPartner) is "sometimes" or "yes" || A(CanSayNo) == "no";
        var concernSigns = A(SafeAtHome) == "unsure" || A(AfraidOfPartner) == "skip" || A(CanSayNo) == "not-always";
        var level = unsafeSigns ? SafetyLevel.Unsafe : concernSigns ? SafetyLevel.Concern : SafetyLevel.NoConcerns;
        return (level, A(SelfHarm) is "yes" or "skip");
    }

    /// <summary>True when every question has one of its own options.</summary>
    public static bool IsComplete(IReadOnlyDictionary<string, string> answers) =>
        answers is not null
        && answers.Count == Questions.Count
        && Questions.All(q => answers.TryGetValue(q.Key, out var a) && q.Options.Any(o => o.Key == a));
}

public sealed record SafetyQuestion(string Key, string Text, IReadOnlyList<SafetyOption> Options);

public sealed record SafetyOption(string Key, string Label);

public enum SafetyLevel
{
    NoConcerns = 1,
    Concern = 2,
    Unsafe = 3,
}
