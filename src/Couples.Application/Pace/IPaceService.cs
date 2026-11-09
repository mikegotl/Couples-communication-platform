using Couples.Domain.Common;

namespace Couples.Application.Pace;

/// <summary>Each user's own pace controls. Only a pause, and when it ends, is visible to the partner.</summary>
public interface IPaceService
{
    Task<PaceSettings> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Result<PaceSettings>> SetQuietHoursAsync(Guid userId, TimeOnly? quietStart, TimeOnly? quietEnd, string timeZoneId, CancellationToken cancellationToken = default);

    Task<Result<PaceSettings>> SetMutedAsync(Guid userId, bool muted, CancellationToken cancellationToken = default);

    Task<Result<PaceSettings>> PauseAsync(Guid userId, DateTimeOffset until, CancellationToken cancellationToken = default);

    Task<Result<PaceSettings>> ResumeAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Whether the user's current partner has paused messages, and until when. Nothing else about their settings.</summary>
    Task<PartnerPace?> GetPartnerPaceAsync(Guid userId, CancellationToken cancellationToken = default);
}

public sealed record PaceSettings(TimeOnly? QuietStart, TimeOnly? QuietEnd, string TimeZoneId, bool Muted, DateTimeOffset? PausedUntil);

public sealed record PartnerPace(DateTimeOffset? PausedUntil);
