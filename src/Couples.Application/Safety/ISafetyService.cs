using Couples.Domain.Common;
using Couples.Domain.Safety;

namespace Couples.Application.Safety;

/// <summary>
/// The user's private safety check and safety mode. Owner-only; no other user, admin view, log,
/// audit event, metric or notification ever reflects it.
/// </summary>
public interface ISafetyService
{
    Task<SafetyStatus> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Result<SafetyStatus>> RecordCheckAsync(Guid userId, IReadOnlyDictionary<string, string> answers, CancellationToken cancellationToken = default);

    Task<SafetyStatus> DeleteAnswersAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<SafetyStatus> SetSafetyModeAsync(Guid userId, bool enabled, CancellationToken cancellationToken = default);
}

/// <summary>
/// Used by shared features (messaging now, joint chat in milestone 12) to stop when either
/// partner may be unsafe. Answers only yes or no; never why.
/// </summary>
public interface ISafetyGate
{
    Task<bool> UserBlocksJointFlowsAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> WorkspaceAllowsJointFlowsAsync(Guid workspaceId, CancellationToken cancellationToken = default);
}

public sealed record SafetyStatus(
    IReadOnlyDictionary<string, string>? Answers,
    SafetyLevel? Level,
    bool SelfHarmSupport,
    DateTimeOffset? CheckedAt,
    bool SafetyMode,
    bool SharingBlocked);
