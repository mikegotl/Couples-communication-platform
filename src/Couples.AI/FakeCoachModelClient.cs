using System.Text.Json;
using System.Text.RegularExpressions;

namespace Couples.AI;

/// <summary>
/// Deterministic, in-process stand-in for a model. Rewrites a few common blaming patterns and
/// returns schema-valid JSON. Sends nothing anywhere; used in development and tests.
/// </summary>
internal sealed partial class FakeCoachModelClient : ICoachModelClient
{
    private static readonly (Regex Pattern, string Replacement)[] Softeners =
    [
        (YouAlways(), "it often feels to me like you"),
        (YouNever(), "I would really like it if you"),
        (WhyCantYou(), "could we find a way to"),
        (YouDont(), "I'm not sure you"),
    ];

    public string Name => "fake";

    public Task<ModelResponse> CompleteAsync(ModelPrompt prompt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        cancellationToken.ThrowIfCancellationRequested();

        var draft = CoachPrompt.ExtractBlock(prompt.User, "MY DRAFT") ?? string.Empty;
        var softened = draft.Trim();
        foreach (var (pattern, replacement) in Softeners)
        {
            softened = pattern.Replace(softened, replacement);
        }

        var payload = new
        {
            declined = false,
            safety_concern = false,
            suggestions = new[]
            {
                new
                {
                    text = Limit($"I'd like to talk about something that matters to me. {softened}"),
                    explanation = "Starting with what matters to you, and describing your own experience, makes it easier for your partner to listen.",
                },
                new
                {
                    text = Limit($"When this happens I feel unsettled, and I'd like us to find a way through it together. {softened}"),
                    explanation = "Naming your feeling and asking to work on it together invites a conversation rather than a defence.",
                },
            },
        };

        var json = JsonSerializer.Serialize(payload);
        return Task.FromResult(new ModelResponse(json, (prompt.System.Length + prompt.User.Length) / 4, json.Length / 4));
    }

    private static string Limit(string text) => text.Length <= CoachOutputValidator.MaxSuggestionLength ? text : text[..CoachOutputValidator.MaxSuggestionLength];

    [GeneratedRegex(@"\byou always\b", RegexOptions.IgnoreCase)]
    private static partial Regex YouAlways();

    [GeneratedRegex(@"\byou never\b", RegexOptions.IgnoreCase)]
    private static partial Regex YouNever();

    [GeneratedRegex(@"\bwhy can'?t you\b", RegexOptions.IgnoreCase)]
    private static partial Regex WhyCantYou();

    [GeneratedRegex(@"\byou don'?t\b", RegexOptions.IgnoreCase)]
    private static partial Regex YouDont();
}
