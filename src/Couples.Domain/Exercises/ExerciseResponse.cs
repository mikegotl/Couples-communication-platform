using Couples.Domain.Common;
using Couples.Domain.Privacy;

namespace Couples.Domain.Exercises;

/// <summary>The owner's answer to one exercise step. Never visible to a partner.</summary>
[Privacy(DataClassification.PrivateSensitive, "Kept with its attempt; erased when the owner deletes the attempt or their account.")]
public sealed class ExerciseResponse : Entity
{
    private ExerciseResponse()
    {
    }

    public Guid AttemptId { get; private set; }

    public Guid OwnerUserId { get; private set; }

    public string StepKey { get; private set; } = string.Empty;

    public string Value { get; private set; } = string.Empty;

    internal static ExerciseResponse Create(ExerciseAttempt attempt, string stepKey, string value, DateTimeOffset now)
    {
        var response = new ExerciseResponse { AttemptId = attempt.Id, OwnerUserId = attempt.OwnerUserId, StepKey = stepKey, Value = value };
        response.Touch(now);
        return response;
    }

    internal void Update(string value, DateTimeOffset now)
    {
        Value = value;
        Touch(now);
    }
}
