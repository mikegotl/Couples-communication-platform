using Couples.Domain.Common;
using Couples.Domain.Privacy;

namespace Couples.Domain.Intake;

public enum IntakeStatus
{
    InProgress,
    Completed,
    Skipped,
}

/// <summary>
/// A user's own onboarding intake: the topics they want to work on and their private notes.
/// One per user, independent of any workspace, so it is never visible to a partner and survives
/// leaving a workspace.
/// </summary>
[Privacy(DataClassification.Personal, "Kept until the owner starts intake over or deletes their account.")]
public sealed class IntakeProfile : Entity
{
    public const int MaxCustomTopics = 5;
    public const int MaxTopics = 12;

    private readonly List<ConcernSelection> selections = [];

    private IntakeProfile()
    {
    }

    public Guid OwnerUserId { get; private set; }

    public IntakeStatus Status { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset? SkippedAt { get; private set; }

    public IReadOnlyList<ConcernSelection> Selections => selections;

    public static Result<IntakeProfile> Start(Guid ownerUserId, DateTimeOffset now)
    {
        if (ownerUserId == Guid.Empty)
        {
            return DomainError.Validation;
        }

        var profile = new IntakeProfile { OwnerUserId = ownerUserId, Status = IntakeStatus.InProgress };
        profile.Touch(now);
        return Result.Ok(profile);
    }

    /// <summary>
    /// Adds a topic. A topic with a content warning is added only once the user has acknowledged
    /// the warning. Catalog topics can be chosen once; custom topics need a label.
    /// </summary>
    public Result<ConcernSelection> AddConcern(ConcernCategory category, string? customLabel, bool warningAcknowledged, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(category);

        if (category.ContentWarning is not null && !warningAcknowledged)
        {
            return DomainError.WarningNotAcknowledged;
        }

        string? label = null;
        if (category.AllowsCustomLabel)
        {
            label = customLabel?.Trim();
            if (string.IsNullOrEmpty(label) || label.Length > ConcernSelection.MaxCustomLabelLength)
            {
                return DomainError.Validation;
            }

            var customCount = selections.Count(s => s.CategoryKey == category.Key);
            if (customCount >= MaxCustomTopics
                || selections.Any(s => s.CategoryKey == category.Key && string.Equals(s.CustomLabel, label, StringComparison.OrdinalIgnoreCase)))
            {
                return DomainError.Conflict;
            }
        }
        else if (selections.Any(s => s.CategoryKey == category.Key))
        {
            return DomainError.Conflict;
        }

        if (selections.Count >= MaxTopics)
        {
            return DomainError.Conflict;
        }

        var selection = ConcernSelection.Create(this, category, label, category.ContentWarning is null ? null : now, now);
        selections.Add(selection);
        if (Status == IntakeStatus.Skipped)
        {
            Status = IntakeStatus.InProgress;
            SkippedAt = null;
        }

        Touch(now);
        return Result.Ok(selection);
    }

    public Result UpdateSummary(Guid selectionId, string? summary, DateTimeOffset now)
    {
        var selection = selections.FirstOrDefault(s => s.Id == selectionId);
        if (selection is null)
        {
            return Result.Fail(DomainError.NotFound);
        }

        var updated = selection.UpdateSummary(summary, now);
        if (updated.Succeeded)
        {
            Touch(now);
        }

        return updated;
    }

    public Result RemoveConcern(Guid selectionId, DateTimeOffset now)
    {
        var selection = selections.FirstOrDefault(s => s.Id == selectionId);
        if (selection is null)
        {
            return Result.Fail(DomainError.NotFound);
        }

        selections.Remove(selection);
        if (Status == IntakeStatus.Completed && selections.Count == 0)
        {
            Status = IntakeStatus.InProgress;
            CompletedAt = null;
        }

        Touch(now);
        return Result.Ok();
    }

    /// <summary>Finishes intake. At least one topic is required; notes are optional.</summary>
    public Result Complete(DateTimeOffset now)
    {
        if (selections.Count == 0)
        {
            return Result.Fail(DomainError.Validation);
        }

        if (Status != IntakeStatus.Completed)
        {
            Status = IntakeStatus.Completed;
            CompletedAt = now;
            SkippedAt = null;
        }

        Touch(now);
        return Result.Ok();
    }

    /// <summary>Skips intake for now. Allowed until intake is completed; it can be resumed later.</summary>
    public Result Skip(DateTimeOffset now)
    {
        if (Status == IntakeStatus.Completed)
        {
            return Result.Fail(DomainError.Conflict);
        }

        Status = IntakeStatus.Skipped;
        SkippedAt = now;
        Touch(now);
        return Result.Ok();
    }
}
