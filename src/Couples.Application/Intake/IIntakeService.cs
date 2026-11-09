using Couples.Domain.Common;
using Couples.Domain.Intake;

namespace Couples.Application.Intake;

/// <summary>
/// A user's own onboarding intake. Every call is scoped to the acting user; there is no method
/// that takes or returns another user's intake, and nothing here depends on workspace membership.
/// </summary>
public interface IIntakeService
{
    IReadOnlyList<ConcernCategoryView> Categories { get; }

    /// <summary>The user's intake, or null if they have not started or skipped it.</summary>
    Task<IntakeView?> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Result<IntakeView>> AddConcernAsync(Guid userId, string categoryKey, string? customLabel, bool warningAcknowledged, CancellationToken cancellationToken = default);

    Task<Result<IntakeView>> UpdateSummaryAsync(Guid userId, Guid concernId, string? summary, CancellationToken cancellationToken = default);

    Task<Result<IntakeView>> RemoveConcernAsync(Guid userId, Guid concernId, CancellationToken cancellationToken = default);

    Task<Result<IntakeView>> CompleteAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Result<IntakeView>> SkipAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Erases the user's intake so they can start over.</summary>
    Task DeleteAsync(Guid userId, CancellationToken cancellationToken = default);
}

public sealed record ConcernCategoryView(string Key, string Name, string Description, string? ContentWarning, bool AllowsCustomLabel);

public sealed record IntakeView(IntakeStatus Status, DateTimeOffset? CompletedAt, DateTimeOffset? SkippedAt, IReadOnlyList<ConcernView> Concerns);

public sealed record ConcernView(Guid Id, string CategoryKey, string Title, string Summary, DateTimeOffset UpdatedAt);
