using System.Diagnostics;
using System.Diagnostics.Metrics;
using Couples.Application.Coaching;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Couples.AI;

/// <summary>
/// The coach: sanitizes input, screens for risk locally, enforces daily caps, calls the model
/// with timeouts and retries, validates the output and falls back safely. It logs and measures
/// outcomes, counts and timings only; no user text or model output is ever logged.
/// </summary>
internal sealed partial class CoachService(
    ICoachModelClient? model,
    ICoachUsageStore usage,
    IOptions<CoachingOptions> options,
    TimeProvider time,
    CoachTelemetry telemetry,
    ILogger<CoachService> logger) : ICommunicationCoach
{
    internal const string UnavailableMessage = "Suggestions aren't available right now. Your draft is unchanged and nothing has been sent.";
    internal const string DeclinedMessage = "The coach can only help with wording a message to your partner. Your draft is unchanged.";
    internal const string SupportMessage = "It sounds like things may be really hard right now. If you or anyone else is in danger, please contact local emergency services. A crisis line or a domestic abuse support service can also help. Your draft is private and has not been sent.";
    internal const string LimitMessage = "You've reached today's limit for suggestions. Your draft is unchanged; you can keep editing it yourself.";
    internal const string InvalidInputMessage = "Write something first. Drafts can be up to {0} characters.";

    private static readonly System.Text.CompositeFormat InvalidInputFormat = System.Text.CompositeFormat.Parse(InvalidInputMessage);
    internal const string SuggestedMessage = "Here are some ideas. Use, edit or ignore them; nothing is sent unless you choose to send it.";

    public bool IsAvailable => model is not null;

    public async Task<CoachResult> SuggestAsync(CoachRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var o = options.Value;
        var started = Stopwatch.GetTimestamp();
        var requestId = Guid.NewGuid();
        var provider = model?.Name ?? "disabled";
        var attempts = 0;

        CoachResult Finish(CoachOutcome outcome, IReadOnlyList<CoachSuggestion>? suggestions = null, string? message = null)
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            telemetry.Record(request.Task, outcome, provider, attempts, elapsed);
            LogOutcome(logger, requestId, request.UserId, request.Task, CoachPrompt.Version, provider, outcome, attempts, (long)elapsed.TotalMilliseconds);
            Couples.Application.Observability.CouplesMetrics.CoachRequests.Add(1, new KeyValuePair<string, object?>("outcome", outcome.ToString()));
            return new CoachResult(outcome, suggestions ?? [], message ?? MessageFor(outcome, o));
        }

        var draft = InputSanitizer.Clean(request.Text);
        var received = request.ReceivedMessage is null ? null : InputSanitizer.Clean(request.ReceivedMessage);
        if (draft.Length == 0 || draft.Length > o.MaxInputCharacters || (received?.Length ?? 0) > o.MaxInputCharacters
            || (request.Task == CoachTask.SuggestReply && string.IsNullOrEmpty(received)))
        {
            return Finish(CoachOutcome.InvalidInput);
        }

        if (SafetyScreen.NeedsSupport(draft, received))
        {
            return Finish(CoachOutcome.SupportSuggested);
        }

        if (model is null)
        {
            return Finish(CoachOutcome.Unavailable);
        }

        var day = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        bool reserved;
        try
        {
            reserved = await usage.TryReserveAsync(request.UserId, day, o.PerUserDailyRequests, o.GlobalDailyRequests, o.GlobalDailyTokens, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // If usage can't be checked, don't spend: fail closed without calling the provider.
            LogAttemptFailed(logger, requestId, 0, "usage-store:" + ex.GetType().Name);
            return Finish(CoachOutcome.Unavailable);
        }

        if (!reserved)
        {
            return Finish(CoachOutcome.LimitReached);
        }

        var (prompt, canary, boundary) = CoachPrompt.Build(request, draft, received, o.MaxOutputTokens);
        using var overall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        overall.CancelAfter(o.OverallTimeout);

        var invalidOutputs = 0;
        int inputTokens = 0, outputTokens = 0;
        try
        {
            while (attempts < o.MaxAttempts)
            {
                attempts++;
                ModelResponse response;
                try
                {
                    using var attempt = CancellationTokenSource.CreateLinkedTokenSource(overall.Token);
                    attempt.CancelAfter(o.AttemptTimeout);
                    response = await model.CompleteAsync(prompt, attempt.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    LogAttemptFailed(logger, requestId, attempts, "timeout");
                    if (overall.IsCancellationRequested || !await BackoffAsync(attempts, overall.Token))
                    {
                        break;
                    }

                    continue;
                }
                catch (CoachTransientException)
                {
                    LogAttemptFailed(logger, requestId, attempts, "transient");
                    if (!await BackoffAsync(attempts, overall.Token))
                    {
                        break;
                    }

                    continue;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Anything else (bad configuration, malformed gateway response) is not retried.
                    LogAttemptFailed(logger, requestId, attempts, ex.GetType().Name);
                    break;
                }

                inputTokens += response.InputTokens;
                outputTokens += response.OutputTokens;

                if (response.Refused)
                {
                    return Finish(CoachOutcome.Declined);
                }

                var validated = CoachOutputValidator.Validate(response.Content, canary, boundary);
                switch (validated.Verdict)
                {
                    case ValidationVerdict.Valid:
                        return Finish(CoachOutcome.Suggested, validated.Suggestions);
                    case ValidationVerdict.Declined:
                        return Finish(CoachOutcome.Declined);
                    case ValidationVerdict.SafetyConcern:
                        return Finish(CoachOutcome.SupportSuggested);
                }

                telemetry.RecordRejectedOutput(provider);
                LogAttemptFailed(logger, requestId, attempts, "invalid-output");
                if (++invalidOutputs > o.MaxInvalidOutputRetries)
                {
                    break;
                }
            }

            return Finish(CoachOutcome.Unavailable);
        }
        finally
        {
            telemetry.RecordTokens(provider, inputTokens, outputTokens);
            if (inputTokens + outputTokens > 0)
            {
                try
                {
                    await usage.RecordTokensAsync(request.UserId, day, inputTokens, outputTokens, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    LogAttemptFailed(logger, requestId, attempts, "usage-record:" + ex.GetType().Name);
                }
            }
        }
    }

    private async Task<bool> BackoffAsync(int attempt, CancellationToken cancellationToken)
    {
        if (attempt >= options.Value.MaxAttempts)
        {
            return false;
        }

        var baseDelay = options.Value.RetryBaseDelay;
        var jitter = Random.Shared.NextDouble() * 0.5 + 0.75;
        var delay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1) * jitter);
        try
        {
            await Task.Delay(delay, time, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static string MessageFor(CoachOutcome outcome, CoachingOptions o) => outcome switch
    {
        CoachOutcome.Suggested => SuggestedMessage,
        CoachOutcome.Declined => DeclinedMessage,
        CoachOutcome.SupportSuggested => SupportMessage,
        CoachOutcome.LimitReached => LimitMessage,
        CoachOutcome.InvalidInput => string.Format(System.Globalization.CultureInfo.InvariantCulture, InvalidInputFormat, o.MaxInputCharacters),
        _ => UnavailableMessage,
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Coach request {RequestId} for user {UserId}: task {Task}, prompt {PromptVersion}, provider {Provider}, outcome {Outcome}, attempts {Attempts}, {ElapsedMs} ms")]
    private static partial void LogOutcome(ILogger logger, Guid requestId, Guid userId, CoachTask task, string promptVersion, string provider, CoachOutcome outcome, int attempts, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Coach request {RequestId} attempt {Attempt} failed: {Reason}")]
    private static partial void LogAttemptFailed(ILogger logger, Guid requestId, int attempt, string reason);
}

/// <summary>Metrics with enum and number tags only. No user ids, text or prompts.</summary>
internal sealed class CoachTelemetry : IDisposable
{
    public const string MeterName = "Couples.Coaching";

    private readonly Meter meter;
    private readonly Counter<long> requests;
    private readonly Counter<long> rejectedOutputs;
    private readonly Counter<long> tokens;
    private readonly Histogram<double> duration;

    public CoachTelemetry(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        meter = meterFactory.Create(MeterName);
        requests = meter.CreateCounter<long>("coach.requests", description: "Coach requests by outcome");
        rejectedOutputs = meter.CreateCounter<long>("coach.rejected_outputs", description: "Model outputs that failed validation");
        tokens = meter.CreateCounter<long>("coach.tokens", unit: "{token}", description: "Model tokens used");
        duration = meter.CreateHistogram<double>("coach.duration", unit: "ms", description: "Time to answer a coach request");
    }

    public void Record(CoachTask task, CoachOutcome outcome, string provider, int attempts, TimeSpan elapsed)
    {
        var tags = new TagList { { "task", task.ToString() }, { "outcome", outcome.ToString() }, { "provider", provider }, { "attempts", attempts } };
        requests.Add(1, tags);
        duration.Record(elapsed.TotalMilliseconds, tags);
    }

    public void RecordRejectedOutput(string provider) => rejectedOutputs.Add(1, new KeyValuePair<string, object?>("provider", provider));

    public void RecordTokens(string provider, int input, int output)
    {
        tokens.Add(input, new KeyValuePair<string, object?>("provider", provider), new KeyValuePair<string, object?>("direction", "input"));
        tokens.Add(output, new KeyValuePair<string, object?>("provider", provider), new KeyValuePair<string, object?>("direction", "output"));
    }

    public void Dispose() => meter.Dispose();
}
