using Couples.Application.Abstractions;
using Couples.Application.Exercises;
using Couples.Domain.Common;
using Couples.Domain.Exercises;
using Couples.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Couples.Infrastructure.Exercises;

internal sealed class ExerciseService(
    CouplesDbContext db,
    IClock clock,
    ExerciseCatalogSync catalog,
    IOptions<ExerciseOptions> options) : IExerciseService
{
    public async Task<IReadOnlyList<ExerciseSummaryView>> ListAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await catalog.EnsureAsync(cancellationToken);
        var definitions = await db.ExerciseDefinitions.AsNoTracking().ToListAsync(cancellationToken);
        var attempts = await Owned(userId).AsNoTracking()
            .Select(a => new { a.Id, a.DefinitionId, a.Status, a.UpdatedAt })
            .ToListAsync(cancellationToken);

        var keyOf = definitions.ToDictionary(d => d.Id, d => d.Key);
        var views = new List<ExerciseSummaryView>();
        foreach (var template in ExerciseCatalog.All.DistinctBy(t => t.Key))
        {
            var current = Current(definitions, template.Key);
            if (current is null)
            {
                continue;
            }

            var mine = attempts.Where(a => keyOf.GetValueOrDefault(a.DefinitionId) == template.Key).ToList();
            var inProgress = mine.Where(a => a.Status == AttemptStatus.InProgress).OrderByDescending(a => a.UpdatedAt).FirstOrDefault();
            views.Add(new ExerciseSummaryView(
                current.Key,
                current.Version,
                current.Title,
                current.Summary,
                current.EstimatedMinutes,
                current.Steps.Count,
                current.ReviewStatus,
                inProgress?.Id,
                mine.Count(a => a.Status == AttemptStatus.Completed)));
        }

        return views;
    }

    public async Task<Result<AttemptView>> StartOrResumeAsync(Guid userId, string exerciseKey, CancellationToken cancellationToken = default)
    {
        await catalog.EnsureAsync(cancellationToken);
        var definitions = await db.ExerciseDefinitions.Where(d => d.Key == exerciseKey).ToListAsync(cancellationToken);
        var ids = definitions.Select(d => d.Id).ToList();

        var existing = await Owned(userId)
            .Where(a => ids.Contains(a.DefinitionId) && a.Status == AttemptStatus.InProgress)
            .OrderByDescending(a => a.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            return Result.Ok(ToView(existing, definitions.Single(d => d.Id == existing.DefinitionId)));
        }

        var current = Current(definitions, exerciseKey);
        if (current is null)
        {
            return DomainError.NotFound;
        }

        var started = ExerciseAttempt.Start(userId, current, clock.UtcNow);
        if (!started.Succeeded)
        {
            return started.Error!.Value;
        }

        db.ExerciseAttempts.Add(started.Value);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Ok(ToView(started.Value, current));
    }

    public async Task<Result<AttemptView>> GetAttemptAsync(Guid userId, Guid attemptId, CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync(userId, attemptId, tracking: false, cancellationToken);
        return loaded is { } x ? Result.Ok(ToView(x.Attempt, x.Definition)) : DomainError.NotFound;
    }

    public Task<Result<AttemptView>> SaveStepAsync(Guid userId, Guid attemptId, string stepKey, string? value, int? moveTo, CancellationToken cancellationToken = default) =>
        MutateAsync(userId, attemptId, (attempt, definition, now) =>
        {
            var saved = attempt.SaveAnswer(definition, stepKey, value, now);
            if (!saved.Succeeded || moveTo is null)
            {
                return saved;
            }

            return attempt.MoveTo(definition, moveTo.Value, now);
        }, cancellationToken);

    public Task<Result<AttemptView>> MoveToStepAsync(Guid userId, Guid attemptId, int stepIndex, CancellationToken cancellationToken = default) =>
        MutateAsync(userId, attemptId, (attempt, definition, now) => attempt.MoveTo(definition, stepIndex, now), cancellationToken);

    public Task<Result<AttemptView>> CompleteAsync(Guid userId, Guid attemptId, CancellationToken cancellationToken = default) =>
        MutateAsync(userId, attemptId, (attempt, definition, now) => attempt.Complete(definition, now), cancellationToken);

    public async Task<IReadOnlyList<AttemptSummaryView>> ListAttemptsAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await (
            from a in Owned(userId).AsNoTracking()
            join d in db.ExerciseDefinitions.AsNoTracking() on a.DefinitionId equals d.Id
            orderby a.UpdatedAt descending
            select new AttemptSummaryView(a.Id, d.Key, d.Title, a.Status, a.CreatedAt, a.UpdatedAt, a.CompletedAt))
        .ToListAsync(cancellationToken);

    public async Task<Result> DeleteAttemptAsync(Guid userId, Guid attemptId, CancellationToken cancellationToken = default)
    {
        await db.ExerciseResponses.Where(r => r.AttemptId == attemptId && r.OwnerUserId == userId).ExecuteDeleteAsync(cancellationToken);
        var deleted = await Owned(userId).Where(a => a.Id == attemptId).ExecuteDeleteAsync(cancellationToken);
        return deleted == 1 ? Result.Ok() : Result.Fail(DomainError.NotFound);
    }

    /// <summary>The only entry point to attempts: always scoped to the owner.</summary>
    private IQueryable<ExerciseAttempt> Owned(Guid userId) =>
        db.ExerciseAttempts.Include(a => a.Responses).Where(a => a.OwnerUserId == userId);

    /// <summary>The version new attempts use: the highest that is not retired (and approved, if required).</summary>
    private ExerciseDefinition? Current(IEnumerable<ExerciseDefinition> definitions, string key) =>
        definitions
            .Where(d => d.Key == key && d.ReviewStatus != ClinicianReviewStatus.Retired)
            .Where(d => !options.Value.RequireClinicianApproval || d.ReviewStatus == ClinicianReviewStatus.ClinicianApproved)
            .MaxBy(d => d.Version);

    private async Task<(ExerciseAttempt Attempt, ExerciseDefinition Definition)?> LoadAsync(Guid userId, Guid attemptId, bool tracking, CancellationToken cancellationToken)
    {
        var query = tracking ? Owned(userId) : Owned(userId).AsNoTracking();
        var attempt = await query.FirstOrDefaultAsync(a => a.Id == attemptId, cancellationToken);
        if (attempt is null)
        {
            return null;
        }

        var definition = await db.ExerciseDefinitions.AsNoTracking().SingleAsync(d => d.Id == attempt.DefinitionId, cancellationToken);
        return (attempt, definition);
    }

    private async Task<Result<AttemptView>> MutateAsync(
        Guid userId,
        Guid attemptId,
        Func<ExerciseAttempt, ExerciseDefinition, DateTimeOffset, Result> change,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(userId, attemptId, tracking: true, cancellationToken);
        if (loaded is not { } x)
        {
            return DomainError.NotFound;
        }

        var result = change(x.Attempt, x.Definition, clock.UtcNow);
        if (!result.Succeeded)
        {
            db.ChangeTracker.Clear();
            return result.Error!.Value;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException || ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The same attempt was saved from another tab or device at the same moment.
            db.ChangeTracker.Clear();
            return DomainError.Conflict;
        }

        return Result.Ok(ToView(x.Attempt, x.Definition));
    }

    private static AttemptView ToView(ExerciseAttempt attempt, ExerciseDefinition definition) => new(
        attempt.Id,
        definition.Key,
        definition.Version,
        definition.Title,
        definition.ReviewStatus,
        attempt.Status,
        attempt.CurrentStep,
        definition.Steps,
        attempt.Responses.ToDictionary(r => r.StepKey, r => r.Value, StringComparer.Ordinal),
        attempt.CreatedAt,
        attempt.UpdatedAt,
        attempt.CompletedAt);
}
