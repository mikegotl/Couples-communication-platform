using Couples.Domain.Common;
using Couples.Domain.Exercises;

namespace Couples.Application.Exercises;

/// <summary>
/// Guided private exercises. Attempts and answers are owner-only: every call takes the acting
/// user's id, and another user's attempt id behaves like one that does not exist.
/// </summary>
public interface IExerciseService
{
    /// <summary>Exercises the user can start, with their own progress on each.</summary>
    Task<IReadOnlyList<ExerciseSummaryView>> ListAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Resumes the user's unfinished attempt at this exercise, or starts one on the current version.</summary>
    Task<Result<AttemptView>> StartOrResumeAsync(Guid userId, string exerciseKey, CancellationToken cancellationToken = default);

    Task<Result<AttemptView>> GetAttemptAsync(Guid userId, Guid attemptId, CancellationToken cancellationToken = default);

    /// <summary>Saves the answer to one step and optionally moves to another step, in one save.</summary>
    Task<Result<AttemptView>> SaveStepAsync(Guid userId, Guid attemptId, string stepKey, string? value, int? moveTo, CancellationToken cancellationToken = default);

    /// <summary>Moves to another step without changing answers (for steps that only have text to read).</summary>
    Task<Result<AttemptView>> MoveToStepAsync(Guid userId, Guid attemptId, int stepIndex, CancellationToken cancellationToken = default);

    Task<Result<AttemptView>> CompleteAsync(Guid userId, Guid attemptId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AttemptSummaryView>> ListAttemptsAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Result> DeleteAttemptAsync(Guid userId, Guid attemptId, CancellationToken cancellationToken = default);
}

/// <summary>Exercise content and clinician review metadata. Contains no user data.</summary>
public interface IExerciseAdminService
{
    Task<IReadOnlyList<ExerciseDefinitionAdminView>> ListDefinitionsAsync(CancellationToken cancellationToken = default);

    Task<Result> RecordReviewAsync(Guid adminUserId, Guid definitionId, ClinicianReviewStatus status, string? reviewer, string? notes, CancellationToken cancellationToken = default);
}

public sealed class ExerciseOptions
{
    public const string SectionName = "Exercises";

    /// <summary>
    /// When true, only clinician-approved versions can be started. Defaults to false until a
    /// clinician reviewer is engaged (an owner decision); unreviewed exercises are labelled.
    /// </summary>
    public bool RequireClinicianApproval { get; set; }
}

public sealed record ExerciseSummaryView(
    string Key,
    int Version,
    string Title,
    string Summary,
    int EstimatedMinutes,
    int StepCount,
    ClinicianReviewStatus ReviewStatus,
    Guid? InProgressAttemptId,
    int CompletedCount);

public sealed record AttemptView(
    Guid Id,
    string ExerciseKey,
    int Version,
    string Title,
    ClinicianReviewStatus ReviewStatus,
    AttemptStatus Status,
    int CurrentStep,
    IReadOnlyList<ExerciseStep> Steps,
    IReadOnlyDictionary<string, string> Answers,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt);

public sealed record AttemptSummaryView(Guid Id, string ExerciseKey, string Title, AttemptStatus Status, DateTimeOffset StartedAt, DateTimeOffset UpdatedAt, DateTimeOffset? CompletedAt);

public sealed record ExerciseDefinitionAdminView(
    Guid Id,
    string Key,
    int Version,
    string Title,
    int StepCount,
    string ContentHash,
    bool MatchesCatalog,
    ClinicianReviewStatus ReviewStatus,
    string? ReviewedBy,
    DateTimeOffset? ReviewedAt,
    string? ReviewNotes,
    DateTimeOffset PublishedAt);
