using Couples.Domain.Common;
using Couples.Domain.Privacy;

namespace Couples.Domain.Pace;

/// <summary>
/// How and when one user wants to hear from their partner. Quiet hours hold notifications until
/// they end; mute turns notifications off (messages still arrive, quietly); pause stops new
/// messages being delivered to this user until a chosen time. Only the pause, and its end time,
/// is visible to the partner, so they know a message will wait.
/// </summary>
[Privacy(DataClassification.Personal, "Kept while the account exists; deleted with the account.")]
public sealed class PacePreferences
{
    public static readonly TimeSpan MaxPause = TimeSpan.FromDays(30);

    private PacePreferences()
    {
    }

    public Guid UserId { get; private set; }

    public TimeOnly? QuietStart { get; private set; }

    public TimeOnly? QuietEnd { get; private set; }

    public string TimeZoneId { get; private set; } = "UTC";

    public bool Muted { get; private set; }

    public DateTimeOffset? PausedUntil { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static PacePreferences Default(Guid userId) => new() { UserId = userId };

    public bool IsPaused(DateTimeOffset now) => PausedUntil is { } until && until > now;

    public Result SetQuietHours(TimeOnly? start, TimeOnly? end, string? timeZoneId, DateTimeOffset now)
    {
        if ((start is null) != (end is null) || (start is not null && start == end) || !TryZone(timeZoneId, out _))
        {
            return Result.Fail(DomainError.Validation);
        }

        QuietStart = start;
        QuietEnd = end;
        TimeZoneId = timeZoneId!;
        UpdatedAt = now;
        return Result.Ok();
    }

    public void SetMuted(bool muted, DateTimeOffset now)
    {
        Muted = muted;
        UpdatedAt = now;
    }

    public Result Pause(DateTimeOffset until, DateTimeOffset now)
    {
        if (until <= now || until - now > MaxPause)
        {
            return Result.Fail(DomainError.Validation);
        }

        PausedUntil = until;
        UpdatedAt = now;
        return Result.Ok();
    }

    public void Resume(DateTimeOffset now)
    {
        PausedUntil = null;
        UpdatedAt = now;
    }

    /// <summary>
    /// When a notification created at <paramref name="now"/> may be shown: immediately, or at the
    /// end of the current quiet hours in the user's time zone. Quiet hours may span midnight.
    /// </summary>
    public DateTimeOffset NotificationVisibleAt(DateTimeOffset now)
    {
        if (QuietStart is not { } start || QuietEnd is not { } end || !TryZone(TimeZoneId, out var zone))
        {
            return now;
        }

        var local = TimeZoneInfo.ConvertTime(now, zone);
        var time = TimeOnly.FromDateTime(local.DateTime);
        var spansMidnight = start > end;
        var inQuiet = spansMidnight ? time >= start || time < end : time >= start && time < end;
        if (!inQuiet)
        {
            return now;
        }

        var endDate = DateOnly.FromDateTime(local.DateTime);
        if (spansMidnight && time >= start)
        {
            endDate = endDate.AddDays(1);
        }

        var endLocal = endDate.ToDateTime(end, DateTimeKind.Unspecified);

        // If the end falls in a daylight-saving gap, move forward to the first valid time.
        while (zone.IsInvalidTime(endLocal))
        {
            endLocal = endLocal.AddMinutes(30);
        }

        return new DateTimeOffset(endLocal, zone.GetUtcOffset(endLocal)).ToUniversalTime();
    }

    public static bool TryZone(string? id, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64)
        {
            return false;
        }

        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }
}
