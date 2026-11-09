using Couples.Application.Abstractions;
using Couples.Application.Pace;
using Couples.Domain.Common;
using Couples.Domain.Pace;
using Couples.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Couples.Infrastructure.Pace;

internal sealed class PaceService(CouplesDbContext db, IClock clock) : IPaceService
{
    public async Task<PaceSettings> GetAsync(Guid userId, CancellationToken cancellationToken = default) =>
        ToView(await db.PacePreferences.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken) ?? PacePreferences.Default(userId), clock.UtcNow);

    public Task<Result<PaceSettings>> SetQuietHoursAsync(Guid userId, TimeOnly? quietStart, TimeOnly? quietEnd, string timeZoneId, CancellationToken cancellationToken = default) =>
        ChangeAsync(userId, (p, now) => p.SetQuietHours(quietStart, quietEnd, timeZoneId, now), cancellationToken);

    public Task<Result<PaceSettings>> SetMutedAsync(Guid userId, bool muted, CancellationToken cancellationToken = default) =>
        ChangeAsync(userId, (p, now) => { p.SetMuted(muted, now); return Result.Ok(); }, cancellationToken);

    public Task<Result<PaceSettings>> PauseAsync(Guid userId, DateTimeOffset until, CancellationToken cancellationToken = default) =>
        ChangeAsync(userId, (p, now) => p.Pause(until, now), cancellationToken);

    public Task<Result<PaceSettings>> ResumeAsync(Guid userId, CancellationToken cancellationToken = default) =>
        ChangeAsync(userId, (p, now) => { p.Resume(now); return Result.Ok(); }, cancellationToken);

    public async Task<PartnerPace?> GetPartnerPaceAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var partnerId = await (
                from mine in db.WorkspaceMemberships
                join theirs in db.WorkspaceMemberships on mine.WorkspaceId equals theirs.WorkspaceId
                where mine.UserId == userId && mine.LeftAt == null && theirs.UserId != userId && theirs.LeftAt == null
                select (Guid?)theirs.UserId)
            .FirstOrDefaultAsync(cancellationToken);
        if (partnerId is null)
        {
            return null;
        }

        var now = clock.UtcNow;
        var pausedUntil = await db.PacePreferences.AsNoTracking()
            .Where(p => p.UserId == partnerId && p.PausedUntil > now)
            .Select(p => p.PausedUntil)
            .FirstOrDefaultAsync(cancellationToken);
        return new PartnerPace(pausedUntil);
    }

    private async Task<Result<PaceSettings>> ChangeAsync(Guid userId, Func<PacePreferences, DateTimeOffset, Result> change, CancellationToken cancellationToken)
    {
        var prefs = await db.PacePreferences.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (prefs is null)
        {
            prefs = PacePreferences.Default(userId);
            db.PacePreferences.Add(prefs);
        }

        var now = clock.UtcNow;
        var changed = change(prefs, now);
        if (!changed.Succeeded)
        {
            return changed.Error!.Value;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return DomainError.Conflict;
        }

        return Result.Ok(ToView(prefs, now));
    }

    private static PaceSettings ToView(PacePreferences p, DateTimeOffset now) =>
        new(p.QuietStart, p.QuietEnd, p.TimeZoneId, p.Muted, p.IsPaused(now) ? p.PausedUntil : null);
}
