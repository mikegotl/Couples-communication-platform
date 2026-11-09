using Couples.Domain.Common;
using Couples.Domain.Privacy;

namespace Couples.Domain.Exercises;

public enum AttemptStatus
{
    InProgress,
    Completed,
}

/// <summary>
/// One user's run through one version of an exercise. Owner only; progress is saved after every
/// step so it can be resumed on any device.
/// </summary>
[Privacy(DataClassification.Personal, "Kept until the owner deletes it or their account.")]
public sealed class ExerciseAttempt : Entity
{
    private readonly List<ExerciseResponse> responses = [];

    private ExerciseAttempt()
    {
    }

    public Guid OwnerUserId { get; private set; }

    public Guid DefinitionId { get; private set; }

    public AttemptStatus Status { get; private set; }

    /// <summary>Index of the step to show when the attempt is resumed.</summary>
    public int CurrentStep { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public IReadOnlyList<ExerciseResponse> Responses => responses;

    public static Result<ExerciseAttempt> Start(Guid ownerUserId, ExerciseDefinition definition, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (ownerUserId == Guid.Empty || definition.Steps.Count == 0)
        {
            return DomainError.Validation;
        }

        var attempt = new ExerciseAttempt { OwnerUserId = ownerUserId, DefinitionId = definition.Id, Status = AttemptStatus.InProgress };
        attempt.Touch(now);
        return Result.Ok(attempt);
    }

    public string? AnswerFor(string stepKey) => responses.FirstOrDefault(r => r.StepKey == stepKey)?.Value;

    /// <summary>Saves (or clears) the answer to one step. Completed attempts can still be edited.</summary>
    public Result SaveAnswer(ExerciseDefinition definition, string stepKey, string? value, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Id != DefinitionId)
        {
            return Result.Fail(DomainError.Validation);
        }

        var step = definition.FindStep(stepKey);
        if (step is null || step.Kind == ExerciseStepKind.Info)
        {
            return Result.Fail(DomainError.Validation);
        }

        var normalized = Normalize(step, value);
        if (normalized is null)
        {
            return Result.Fail(DomainError.Validation);
        }

        var existing = responses.FirstOrDefault(r => r.StepKey == stepKey);
        if (normalized.Length == 0)
        {
            if (existing is not null)
            {
                responses.Remove(existing);
            }
        }
        else if (existing is null)
        {
            responses.Add(ExerciseResponse.Create(this, stepKey, normalized, now));
        }
        else
        {
            existing.Update(normalized, now);
        }

        Touch(now);
        return Result.Ok();
    }

    /// <summary>Moves to another step. Moving forward past a required step needs an answer.</summary>
    public Result MoveTo(ExerciseDefinition definition, int stepIndex, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (stepIndex < 0 || stepIndex >= definition.Steps.Count)
        {
            return Result.Fail(DomainError.Validation);
        }

        for (var i = CurrentStep; i < stepIndex; i++)
        {
            if (!IsAnswered(definition.Steps[i]))
            {
                return Result.Fail(DomainError.Validation);
            }
        }

        CurrentStep = stepIndex;
        Touch(now);
        return Result.Ok();
    }

    public Result Complete(ExerciseDefinition definition, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Steps.Any(s => !IsAnswered(s)))
        {
            return Result.Fail(DomainError.Validation);
        }

        if (Status != AttemptStatus.Completed)
        {
            Status = AttemptStatus.Completed;
            CompletedAt = now;
        }

        CurrentStep = definition.Steps.Count - 1;
        Touch(now);
        return Result.Ok();
    }

    public bool IsAnswered(ExerciseStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return !step.NeedsAnswer || !string.IsNullOrEmpty(AnswerFor(step.Key));
    }

    /// <summary>Choice answers are option keys, one per line. Returns null when the value is invalid.</summary>
    private static string? Normalize(ExerciseStep step, string? value)
    {
        switch (step.Kind)
        {
            case ExerciseStepKind.Text:
                var text = value?.Trim() ?? string.Empty;
                return text.Length <= step.MaxLength ? text : null;

            case ExerciseStepKind.Choice:
            case ExerciseStepKind.MultiChoice:
                var keys = (value ?? string.Empty)
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (keys.Any(k => step.Choices.All(o => o.Key != k)) || (step.Kind == ExerciseStepKind.Choice && keys.Count > 1))
                {
                    return null;
                }

                // Keep the authored order so answers read the same everywhere.
                return string.Join('\n', step.Choices.Where(o => keys.Contains(o.Key)).Select(o => o.Key));

            default:
                return null;
        }
    }
}
