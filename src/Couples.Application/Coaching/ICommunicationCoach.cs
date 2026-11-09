namespace Couples.Application.Coaching;

/// <summary>
/// Provider-neutral AI coach. It suggests respectful wording for the user to review; it never
/// sends anything, never stores content, and never throws for provider problems (it returns
/// <see cref="CoachOutcome.Unavailable"/> instead) so callers always keep the user's own text.
/// </summary>
public interface ICommunicationCoach
{
    Task<CoachResult> SuggestAsync(CoachRequest request, CancellationToken cancellationToken = default);
}

public enum CoachTask
{
    /// <summary>Suggest kinder, clearer wording for the user's own draft.</summary>
    RephraseDraft,

    /// <summary>Suggest a respectful reply to a message the user received (milestone 08).</summary>
    SuggestReply,
}

/// <param name="UserId">The acting user; used for limits and telemetry, never sent to a provider.</param>
/// <param name="Text">The user's own draft.</param>
/// <param name="ReceivedMessage">For <see cref="CoachTask.SuggestReply"/>: the message being answered.</param>
public sealed record CoachRequest(Guid UserId, CoachTask Task, string Text, string? ReceivedMessage = null);

public enum CoachOutcome
{
    /// <summary>One or more suggestions passed validation.</summary>
    Suggested,

    /// <summary>The request was outside what the coach does, or the provider refused.</summary>
    Declined,

    /// <summary>The text suggested someone may be at risk; support information is shown instead.</summary>
    SupportSuggested,

    /// <summary>The coach is off, the provider failed or timed out, or output failed validation.</summary>
    Unavailable,

    /// <summary>A daily usage or cost limit was reached.</summary>
    LimitReached,

    /// <summary>The input was empty or too long.</summary>
    InvalidInput,
}

public sealed record CoachSuggestion(string Text, string Explanation);

/// <param name="Message">Fixed, user-facing text for the outcome. Never model output.</param>
public sealed record CoachResult(CoachOutcome Outcome, IReadOnlyList<CoachSuggestion> Suggestions, string Message);

/// <summary>Daily usage counters that enforce request and cost caps. Holds counts only, never text.</summary>
public interface ICoachUsageStore
{
    /// <summary>Counts one request for the user if they and the whole service are under today's limits.</summary>
    Task<bool> TryReserveAsync(Guid userId, DateOnly day, int perUserLimit, int globalRequestLimit, long globalTokenLimit, CancellationToken cancellationToken = default);

    Task RecordTokensAsync(Guid userId, DateOnly day, int inputTokens, int outputTokens, CancellationToken cancellationToken = default);
}
