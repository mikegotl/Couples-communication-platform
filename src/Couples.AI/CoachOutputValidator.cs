using System.Text.Json;
using System.Text.RegularExpressions;
using Couples.Application.Coaching;

namespace Couples.AI;

internal enum ValidationVerdict
{
    Valid,
    Declined,
    SafetyConcern,
    Invalid,
}

internal sealed record ValidatedOutput(ValidationVerdict Verdict, IReadOnlyList<CoachSuggestion> Suggestions, string? Reason = null);

/// <summary>
/// Treats model output as untrusted. Accepts only JSON in the expected shape, within length
/// limits, that does not leak the instructions, does not carry links or contact details, and
/// does not diagnose, label or assign blame.
/// </summary>
internal static partial class CoachOutputValidator
{
    public const int MaxSuggestionLength = 2_000;
    public const int MaxExplanationLength = 500;
    public const int MaxSuggestions = 3;

    /// <summary>Words and phrases the coach must never use in its own suggestions or explanations.</summary>
    private static readonly string[] Forbidden =
    [
        "narcissis", "gaslight", "toxic", "abusive", "abuser", "disorder", "diagnos", "psychopath", "sociopath",
        "bipolar", "borderline personality", "manipulative", "codependen", "therapist", "therapy", "counsell", "counsel",
        "it's your fault", "it is your fault", "it's their fault", "it is their fault", "you are right", "you're right",
        "your partner is wrong", "they are wrong", "you should leave", "break up with",
        "system prompt", "my instructions", "internal reference",
    ];

    private static readonly HashSet<string> TopLevel = new(StringComparer.Ordinal) { "declined", "safety_concern", "suggestions" };
    private static readonly HashSet<string> SuggestionFields = new(StringComparer.Ordinal) { "text", "explanation" };

    public static ValidatedOutput Validate(string? content, string canary, string boundary)
    {
        if (string.IsNullOrWhiteSpace(content) || content.Contains(canary, StringComparison.OrdinalIgnoreCase) || content.Contains(boundary, StringComparison.OrdinalIgnoreCase))
        {
            return Invalid("empty or leaked");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(StripFence(content), new JsonDocumentOptions { MaxDepth = 8 });
        }
        catch (JsonException)
        {
            return Invalid("not json");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Any(p => !TopLevel.Contains(p.Name)))
            {
                return Invalid("shape");
            }

            if (!TryBool(root, "declined", out var declined) || !TryBool(root, "safety_concern", out var safety)
                || !root.TryGetProperty("suggestions", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                return Invalid("shape");
            }

            if (safety)
            {
                return new ValidatedOutput(ValidationVerdict.SafetyConcern, []);
            }

            if (declined)
            {
                return new ValidatedOutput(ValidationVerdict.Declined, []);
            }

            var count = list.GetArrayLength();
            if (count is 0 or > MaxSuggestions)
            {
                return Invalid("count");
            }

            var suggestions = new List<CoachSuggestion>(count);
            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || item.EnumerateObject().Any(p => !SuggestionFields.Contains(p.Name))
                    || !TryString(item, "text", MaxSuggestionLength, out var text) || !TryString(item, "explanation", MaxExplanationLength, out var explanation))
                {
                    return Invalid("suggestion shape");
                }

                if (!IsAcceptable(text) || !IsAcceptable(explanation))
                {
                    return Invalid("content");
                }

                suggestions.Add(new CoachSuggestion(text, explanation));
            }

            return new ValidatedOutput(ValidationVerdict.Valid, suggestions);
        }
    }

    internal static bool IsAcceptable(string value)
    {
        if (value.Any(c => char.IsControl(c) && c is not '\n' and not '\t') || value.Any(c => char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format))
        {
            return false;
        }

        if (LinkPattern().IsMatch(value) || EmailPattern().IsMatch(value) || LooksLikePhoneNumber(value) || MarkupPattern().IsMatch(value))
        {
            return false;
        }

        return !Forbidden.Any(f => value.Contains(f, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A run of digits and separators holding nine or more digits. Dates and times such as
    /// "2026-11-09" or "18:30" have fewer digits and are allowed.
    /// </summary>
    private static bool LooksLikePhoneNumber(string value) =>
        PhonePattern().Matches(value).Any(m => m.Value.Count(char.IsAsciiDigit) >= 9);

    private static string StripFence(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLine = trimmed.IndexOf('\n', StringComparison.Ordinal);
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstLine > 0 && lastFence > firstLine)
            {
                return trimmed[(firstLine + 1)..lastFence];
            }
        }

        return trimmed;
    }

    private static bool TryBool(JsonElement element, string name, out bool value)
    {
        value = false;
        if (!element.TryGetProperty(name, out var property) || property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = property.GetBoolean();
        return true;
    }

    private static bool TryString(JsonElement element, string name, int maxLength, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString()!.Trim();
        return value.Length is > 0 and <= MaxSuggestionLength && value.Length <= maxLength;
    }

    private static ValidatedOutput Invalid(string reason) => new(ValidationVerdict.Invalid, [], reason);

    [GeneratedRegex(@"(https?://|www\.|\b[a-z0-9-]+\.(com|net|org|io|ai|co|app|xyz)\b)", RegexOptions.IgnoreCase)]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"\+?\d[\d\s().-]{7,}\d")]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"<\s*/?\s*(script|system|assistant|user|iframe|img)\b|\[\s*(system|assistant)\s*\]", RegexOptions.IgnoreCase)]
    private static partial Regex MarkupPattern();
}
