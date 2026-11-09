using Couples.Application.Coaching;
using Couples.Application.Messaging;
using Couples.Domain.Common;

namespace Couples.AI;

internal sealed class DraftAssistant(IMessagingService messages, ICommunicationCoach coach) : IDraftAssistant
{
    public bool IsAvailable => coach.IsAvailable;

    public async Task<Result<CoachResult>> SuggestRewriteAsync(Guid userId, Guid draftId, CancellationToken cancellationToken = default)
    {
        // Ownership is enforced by the messaging service: another user's draft is "not found".
        var draft = await messages.GetDraftAsync(userId, draftId, cancellationToken);
        if (!draft.Succeeded)
        {
            return draft.Error!.Value;
        }

        if (draft.Value.Status != "Draft")
        {
            return DomainError.Conflict;
        }

        return Result.Ok(await coach.SuggestAsync(new CoachRequest(userId, CoachTask.RephraseDraft, draft.Value.Body), cancellationToken));
    }
}
