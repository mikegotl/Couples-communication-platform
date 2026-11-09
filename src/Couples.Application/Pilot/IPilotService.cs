using Couples.Domain.Common;

namespace Couples.Application.Pilot;

public sealed record PilotStatus(bool Enrolled, bool NeedsReconsent, bool Withdrawn, DateTimeOffset? ConsentedAt);

public sealed class PilotOptions
{
    public const string SectionName = "Pilot";

    /// <summary>
    /// SHA-256 (hex) of the enrollment code given to invited participants. Empty means no code is
    /// needed (Development). Never store the code itself in configuration.
    /// </summary>
    public string? AccessCodeSha256 { get; set; }

    /// <summary>The most participants the pilot accepts.</summary>
    public int MaxParticipants { get; set; } = 60;
}

/// <summary>Consent-based enrollment in the private pilot (ADR-0014).</summary>
public interface IPilotService
{
    Task<PilotStatus> GetStatusAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records agreement to the given document versions (they must be the current ones). Returns
    /// <see cref="DomainError.Validation"/> for a wrong access code and
    /// <see cref="DomainError.PilotFull"/> when the pilot has no places left.
    /// </summary>
    Task<Result<PilotStatus>> EnrollAsync(Guid userId, string termsVersion, string privacyNoticeVersion, string? accessCode, CancellationToken cancellationToken = default);

    Task<PilotStatus> WithdrawAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Server-side gate used by the web app when <c>PilotEnrollmentRequired</c> is on.</summary>
    Task<bool> IsEnrolledAsync(Guid userId, CancellationToken cancellationToken = default);
}
