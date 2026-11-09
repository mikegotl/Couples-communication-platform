using System.Text.Json;
using Couples.Domain.Common;
using Couples.Domain.Privacy;

namespace Couples.Domain.Safety;

/// <summary>
/// One user's private safety check answers and whether they turned on safety mode. Visible only
/// to that user. Nothing here is ever shown, notified, logged, audited or otherwise signalled to
/// the partner, an admin or telemetry; the partner cannot tell whether it exists.
/// </summary>
[Privacy(DataClassification.PrivateSensitive, "Until the user deletes their answers or turns safety mode off and deletes them; erased with the account.")]
public sealed class PersonalSafety
{
    private PersonalSafety()
    {
    }

    public Guid UserId { get; private set; }

    /// <summary>Answers to the current check as JSON (question key to option key), or null.</summary>
    public string? AnswersJson { get; private set; }

    public int? ContentVersion { get; private set; }

    public SafetyLevel? Level { get; private set; }

    public bool SelfHarmSupport { get; private set; }

    public DateTimeOffset? CheckedAt { get; private set; }

    /// <summary>
    /// While on: this user can't share messages with their partner, gets no notices, sees partner
    /// messages only when they choose to, and joint features are unavailable to the workspace.
    /// </summary>
    public bool SafetyMode { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static PersonalSafety For(Guid userId) => new() { UserId = userId };

    public Result RecordCheck(IReadOnlyDictionary<string, string> answers, DateTimeOffset now)
    {
        if (!SafetyContent.IsComplete(answers))
        {
            return Result.Fail(DomainError.Validation);
        }

        var (level, selfHarm) = SafetyContent.Assess(answers);
        AnswersJson = JsonSerializer.Serialize(answers.OrderBy(a => a.Key, StringComparer.Ordinal).ToDictionary(a => a.Key, a => a.Value));
        ContentVersion = SafetyContent.Version;
        Level = level;
        SelfHarmSupport = selfHarm;
        CheckedAt = now;
        UpdatedAt = now;
        return Result.Ok();
    }

    public void ClearCheck(DateTimeOffset now)
    {
        AnswersJson = null;
        ContentVersion = null;
        Level = null;
        SelfHarmSupport = false;
        CheckedAt = null;
        UpdatedAt = now;
    }

    public void SetSafetyMode(bool on, DateTimeOffset now)
    {
        SafetyMode = on;
        UpdatedAt = now;
    }

    /// <summary>Whether shared features involving this user should stop: safety mode, or an unsafe answer.</summary>
    public bool BlocksJointFlows => SafetyMode || Level == SafetyLevel.Unsafe;
}
