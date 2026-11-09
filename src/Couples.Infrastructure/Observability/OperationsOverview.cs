using Couples.Application.Abstractions;
using Couples.Application.Observability;
using Couples.Domain.Privacy;
using Couples.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Couples.Infrastructure.Observability;

internal sealed class OperationsOverview(CouplesDbContext db, IClock clock) : IOperationsOverview
{
    public async Task<OperationsSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var weekAgo = now.AddDays(-7);
        var monthAgo = now.AddDays(-30);

        return new OperationsSnapshot(
            Suppress(await db.Users.LongCountAsync(cancellationToken)),
            Suppress(await db.PilotEnrollments.LongCountAsync(e => e.WithdrawnAt == null, cancellationToken)),
            Suppress(await db.PilotEnrollments.LongCountAsync(e => e.WithdrawnAt != null, cancellationToken)),
            Suppress(await db.Workspaces.LongCountAsync(w => db.WorkspaceMemberships.Count(m => m.WorkspaceId == w.Id && m.LeftAt == null) == 2, cancellationToken)),
            Suppress(await db.SharedMessages.LongCountAsync(m => m.SharedAt > weekAgo, cancellationToken)),
            Suppress(await db.Feedback.LongCountAsync(f => f.CreatedAt > monthAgo, cancellationToken)),
            Suppress(await db.DataDeletionRequests.LongCountAsync(r => r.Status == DeletionStatus.Pending, cancellationToken)));
    }

    private static long? Suppress(long count) => count < OperationsSnapshot.SuppressBelow ? null : count;
}
