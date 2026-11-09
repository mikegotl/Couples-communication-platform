namespace Couples.AI;

/// <summary>One prompt for a model: fixed instructions, the user's text as data, and the output schema.</summary>
internal sealed record ModelPrompt(string PromptVersion, string System, string User, string JsonSchema, int MaxOutputTokens);

internal sealed record ModelResponse(string Content, int InputTokens, int OutputTokens, bool Refused = false);

/// <summary>The only seam to a model. Implementations must not log prompt or response text.</summary>
internal interface ICoachModelClient
{
    string Name { get; }

    Task<ModelResponse> CompleteAsync(ModelPrompt prompt, CancellationToken cancellationToken);
}

/// <summary>A failure worth retrying: rate limits, server errors, connection problems.</summary>
internal sealed class CoachTransientException : Exception
{
    public CoachTransientException()
    {
    }

    public CoachTransientException(string message)
        : base(message)
    {
    }

    public CoachTransientException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
