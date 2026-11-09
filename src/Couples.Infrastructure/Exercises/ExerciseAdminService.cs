using Couples.Application.Abstractions;
using Couples.Application.Exercises;
using Couples.Domain.Auditing;
using Couples.Domain.Common;
using Couples.Domain.Exercises;
using Couples.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Couples.Infrastructure.Exercises;

/// <summary>Content and review metadata only. Callers must already have checked the admin role.</summary>
internal sealed class ExerciseAdminService(CouplesDbContext db, IClock clock, ExerciseCatalogSync catalog) : IExerciseAdminService
{
    public async Task<IReadOnlyList<ExerciseDefinitionAdminView>> ListDefinitionsAsync(CancellationToken cancellationToken = default)
    {
        await catalog.EnsureAsync(cancellationToken);
        var definitions = await db.ExerciseDefinitions.AsNoTracking()
            .OrderBy(d => d.Key).ThenByDescending(d => d.Version)
            .ToListAsync(cancellationToken);

        return [.. definitions.Select(d =>
        {
            var template = ExerciseCatalog.All.FirstOrDefault(t => t.Key == d.Key && t.Version == d.Version);
            return new ExerciseDefinitionAdminView(
                d.Id, d.Key, d.Version, d.Title, d.Steps.Count, d.ContentHash,
                template is not null && ExerciseDefinition.HashOf(template) == d.ContentHash,
                d.ReviewStatus, d.ReviewedBy, d.ReviewedAt, d.ReviewNotes, d.CreatedAt);
        })];
    }

    public async Task<Result> RecordReviewAsync(Guid adminUserId, Guid definitionId, ClinicianReviewStatus status, string? reviewer, string? notes, CancellationToken cancellationToken = default)
    {
        var definition = await db.ExerciseDefinitions.FirstOrDefaultAsync(d => d.Id == definitionId, cancellationToken);
        if (definition is null)
        {
            return Result.Fail(DomainError.NotFound);
        }

        var now = clock.UtcNow;
        var recorded = definition.RecordReview(status, reviewer, notes, now);
        if (!recorded.Succeeded)
        {
            return recorded;
        }

        db.AuditEvents.Add(AuditEvent.Record(adminUserId, AuditActions.ExerciseReviewed, null, definition.Id, now));
        await db.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
