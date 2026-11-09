using Couples.Application.Abstractions;
using Couples.Application.Intake;
using Couples.Domain.Common;
using Couples.Domain.Intake;
using Couples.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Couples.Infrastructure.Intake;

internal sealed partial class IntakeService(CouplesDbContext db, IClock clock, ILogger<IntakeService> logger) : IIntakeService
{
    private static readonly IReadOnlyList<ConcernCategoryView> CategoryViews =
        [.. ConcernCategory.All.Select(c => new ConcernCategoryView(c.Key, c.Name, c.Description, c.ContentWarning, c.AllowsCustomLabel))];

    public IReadOnlyList<ConcernCategoryView> Categories => CategoryViews;

    public async Task<IntakeView?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var profile = await Owned(userId).AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return profile is null ? null : ToView(profile);
    }

    public async Task<Result<IntakeView>> AddConcernAsync(Guid userId, string categoryKey, string? customLabel, bool warningAcknowledged, CancellationToken cancellationToken = default)
    {
        var category = ConcernCategory.Find(categoryKey);
        if (category is null)
        {
            return DomainError.Validation;
        }

        return await MutateAsync(
            userId,
            createIfMissing: true,
            (p, now) =>
            {
                var added = p.AddConcern(category, customLabel, warningAcknowledged, now);
                return added.Succeeded ? Result.Ok() : Result.Fail(added.Error!.Value);
            },
            cancellationToken);
    }

    public Task<Result<IntakeView>> UpdateSummaryAsync(Guid userId, Guid concernId, string? summary, CancellationToken cancellationToken = default) =>
        MutateAsync(userId, createIfMissing: false, (p, now) => p.UpdateSummary(concernId, summary, now), cancellationToken);

    public Task<Result<IntakeView>> RemoveConcernAsync(Guid userId, Guid concernId, CancellationToken cancellationToken = default) =>
        MutateAsync(userId, createIfMissing: false, (p, now) => p.RemoveConcern(concernId, now), cancellationToken);

    public async Task<Result<IntakeView>> CompleteAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var result = await MutateAsync(userId, createIfMissing: false, (p, now) => p.Complete(now), cancellationToken);
        if (result.Succeeded)
        {
            LogStatus(logger, userId, IntakeStatus.Completed, result.Value.Concerns.Count);
        }

        return result;
    }

    public async Task<Result<IntakeView>> SkipAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var result = await MutateAsync(userId, createIfMissing: true, (p, now) => p.Skip(now), cancellationToken);
        if (result.Succeeded)
        {
            LogStatus(logger, userId, IntakeStatus.Skipped, result.Value.Concerns.Count);
        }

        return result;
    }

    public async Task DeleteAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await db.ConcernSelections.Where(s => s.OwnerUserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.IntakeProfiles.Where(p => p.OwnerUserId == userId).ExecuteDeleteAsync(cancellationToken);
        LogReset(logger, userId);
    }

    /// <summary>The only entry point to intake data: always scoped to the owner.</summary>
    private IQueryable<IntakeProfile> Owned(Guid userId) =>
        db.IntakeProfiles.Include(p => p.Selections).Where(p => p.OwnerUserId == userId);

    private async Task<Result<IntakeView>> MutateAsync(
        Guid userId,
        bool createIfMissing,
        Func<IntakeProfile, DateTimeOffset, Result> change,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var profile = await Owned(userId).FirstOrDefaultAsync(cancellationToken);
        if (profile is null)
        {
            if (!createIfMissing)
            {
                return DomainError.NotFound;
            }

            var started = IntakeProfile.Start(userId, now);
            if (!started.Succeeded)
            {
                return started.Error!.Value;
            }

            profile = started.Value;
            db.IntakeProfiles.Add(profile);
        }

        var result = change(profile, now);
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
            // Another request changed or created this intake at the same moment.
            db.ChangeTracker.Clear();
            return DomainError.Conflict;
        }

        return Result.Ok(ToView(profile));
    }

    private static IntakeView ToView(IntakeProfile profile) => new(
        profile.Status,
        profile.CompletedAt,
        profile.SkippedAt,
        [.. profile.Selections
            .OrderBy(s => ConcernCategory.Find(s.CategoryKey)?.SortOrder ?? int.MaxValue)
            .ThenBy(s => s.CreatedAt)
            .Select(s => new ConcernView(s.Id, s.CategoryKey, s.CustomLabel ?? ConcernCategory.Find(s.CategoryKey)?.Name ?? s.CategoryKey, s.Summary, s.UpdatedAt))]);

    // Ids and counts only: topic names, custom labels and notes never reach logs.
    [LoggerMessage(Level = LogLevel.Information, Message = "Intake for user {UserId} is now {Status} with {TopicCount} topics")]
    private static partial void LogStatus(ILogger logger, Guid userId, IntakeStatus status, int topicCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Intake for user {UserId} was erased to start over")]
    private static partial void LogReset(ILogger logger, Guid userId);
}
