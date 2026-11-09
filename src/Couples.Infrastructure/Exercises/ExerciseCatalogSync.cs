using System.Collections.Concurrent;
using Couples.Application.Abstractions;
using Couples.Domain.Exercises;
using Couples.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Couples.Infrastructure.Exercises;

/// <summary>
/// Publishes catalog versions that are not yet in the database. Existing versions are never
/// changed: review status belongs to administrators, and content is immutable per version.
/// Runs once per database per process, on first use.
/// </summary>
internal sealed partial class ExerciseCatalogSync(CouplesDbContext db, IClock clock, ILogger<ExerciseCatalogSync> logger)
{
    private static readonly ConcurrentDictionary<string, bool> Synced = new(StringComparer.Ordinal);
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task EnsureAsync(CancellationToken cancellationToken)
    {
        var database = db.Database.GetConnectionString() ?? string.Empty;
        if (Synced.ContainsKey(database))
        {
            return;
        }

        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (Synced.ContainsKey(database))
            {
                return;
            }

            await SyncAsync(cancellationToken);
            Synced[database] = true;
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task SyncAsync(CancellationToken cancellationToken)
    {
        var existing = await db.ExerciseDefinitions.AsNoTracking()
            .Select(d => new { d.Id, d.Key, d.Version, d.ContentHash })
            .ToListAsync(cancellationToken);

        foreach (var template in ExerciseCatalog.All)
        {
            var match = existing.FirstOrDefault(d => d.Key == template.Key && d.Version == template.Version);
            if (match is null)
            {
                db.ExerciseDefinitions.Add(ExerciseDefinition.Publish(template, clock.UtcNow));
                LogPublished(logger, template.Key, template.Version);
            }
            else if (match.ContentHash != ExerciseDefinition.HashOf(template))
            {
                LogContentDrift(logger, template.Key, template.Version);
            }
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another instance published the same versions at the same moment.
            db.ChangeTracker.Clear();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Published exercise {Key} version {Version}")]
    private static partial void LogPublished(ILogger logger, string key, int version);

    [LoggerMessage(Level = LogLevel.Error, Message = "Exercise {Key} version {Version} in the catalog differs from the published content. Publish a new version instead of editing a published one.")]
    private static partial void LogContentDrift(ILogger logger, string key, int version);
}
