using System.Globalization;
using Couples.Application.Abstractions;
using Couples.Application.Feedback;
using Couples.Application.Observability;
using Couples.Domain.Common;
using Couples.Domain.Feedback;
using Couples.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Couples.Infrastructure.Feedback;

/// <summary>
/// Stores pilot feedback. The daily limit is counted in memory so anonymous entries don't need a
/// stored link to their author. It resets if the app restarts, which is acceptable for a limit
/// that only guards against floods.
/// </summary>
internal sealed class FeedbackService(CouplesDbContext db, IClock clock, IMemoryCache cache) : IFeedbackService
{
    public async Task<Result> SubmitAsync(Guid userId, FeedbackArea area, int? rating, string? text, bool allowContact, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var key = "feedback:" + userId.ToString("N") + ":" + now.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var count = cache.GetOrCreate(key, e =>
        {
            e.AbsoluteExpiration = now.UtcDateTime.Date.AddDays(1);
            return new Counter();
        })!;
        if (count.Value >= IFeedbackService.DailyLimit)
        {
            return Result.Fail(DomainError.RateLimited);
        }

        var entry = FeedbackEntry.Create(area, rating, text, allowContact ? userId : null, now);
        if (!entry.Succeeded)
        {
            return Result.Fail(entry.Error!.Value);
        }

        db.Feedback.Add(entry.Value);
        await db.SaveChangesAsync(cancellationToken);
        Interlocked.Increment(ref count.Value);
        CouplesMetrics.FeedbackSubmitted.Add(1, new KeyValuePair<string, object?>("area", area.ToString()));
        return Result.Ok();
    }

    public async Task<IReadOnlyList<FeedbackView>> ListAsync(int take, CancellationToken cancellationToken = default) =>
        await db.Feedback.AsNoTracking()
            .OrderByDescending(f => f.CreatedAt)
            .Take(Math.Clamp(take, 1, 500))
            .Select(f => new FeedbackView(f.Id, f.CreatedAt, f.Area, f.Rating, f.Text, f.ContactUserId != null))
            .ToListAsync(cancellationToken);

    private sealed class Counter
    {
        public int Value;
    }
}
