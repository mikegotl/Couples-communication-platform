using Couples.Application.Coaching;
using Couples.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Couples.Infrastructure.Coaching;

/// <summary>
/// Daily counters in PostgreSQL. The per-user cap is enforced atomically by a conditional
/// upsert; the service-wide request and token caps are checked just before and can be
/// exceeded by at most the number of requests in flight at that moment.
/// </summary>
internal sealed class CoachUsageStore(CouplesDbContext db) : ICoachUsageStore
{
    public async Task<bool> TryReserveAsync(Guid userId, DateOnly day, int perUserLimit, int globalRequestLimit, long globalTokenLimit, CancellationToken cancellationToken = default)
    {
        if (perUserLimit <= 0)
        {
            return false;
        }

        var totals = await db.CoachingUsage
            .Where(u => u.Day == day)
            .GroupBy(u => u.Day)
            .Select(g => new { Requests = g.Sum(u => u.Requests), Tokens = g.Sum(u => u.InputTokens + u.OutputTokens) })
            .FirstOrDefaultAsync(cancellationToken);
        if (totals is not null && (totals.Requests >= globalRequestLimit || totals.Tokens >= globalTokenLimit))
        {
            return false;
        }

        var counted = await db.Database.SqlQuery<int>($"""
            INSERT INTO couples.coaching_usage ("UserId", "Day", "Requests", "InputTokens", "OutputTokens")
            VALUES ({userId}, {day}, 1, 0, 0)
            ON CONFLICT ("UserId", "Day") DO UPDATE SET "Requests" = coaching_usage."Requests" + 1
            WHERE coaching_usage."Requests" < {perUserLimit}
            RETURNING "Requests" AS "Value"
            """).ToListAsync(cancellationToken);
        return counted.Count == 1;
    }

    public async Task RecordTokensAsync(Guid userId, DateOnly day, int inputTokens, int outputTokens, CancellationToken cancellationToken = default) =>
        await db.CoachingUsage
            .Where(u => u.UserId == userId && u.Day == day)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(u => u.InputTokens, u => u.InputTokens + inputTokens)
                    .SetProperty(u => u.OutputTokens, u => u.OutputTokens + outputTokens),
                cancellationToken);
}
