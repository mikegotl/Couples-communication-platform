using Couples.Domain.Privacy;

namespace Couples.Domain.Coaching;

/// <summary>
/// How many coach requests and model tokens one user used on one day, to enforce request and
/// cost caps. Counts only: no text, prompts or suggestions are ever stored.
/// </summary>
[Privacy(DataClassification.Personal, "Purged 90 days after the day it counts; deleted with the account.")]
public sealed class CoachingUsage
{
    private CoachingUsage()
    {
    }

    public Guid UserId { get; private set; }

    public DateOnly Day { get; private set; }

    public int Requests { get; private set; }

    public long InputTokens { get; private set; }

    public long OutputTokens { get; private set; }
}
