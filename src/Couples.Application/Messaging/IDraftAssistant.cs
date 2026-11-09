using Couples.Application.Coaching;
using Couples.Domain.Common;

namespace Couples.Application.Messaging;

/// <summary>
/// Asks the coach for kinder wording of one of the user's own drafts. Suggestions are returned
/// to the author only; they are never stored, never applied automatically and never shared.
/// </summary>
public interface IDraftAssistant
{
    bool IsAvailable { get; }

    Task<Result<CoachResult>> SuggestRewriteAsync(Guid userId, Guid draftId, CancellationToken cancellationToken = default);
}
