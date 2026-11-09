namespace Couples.AI;

public enum CoachProvider
{
    /// <summary>The coach is off; every request returns Unavailable without sending anything.</summary>
    Disabled,

    /// <summary>Deterministic in-process suggestions for development and tests. Sends nothing.</summary>
    Fake,

    /// <summary>
    /// A model gateway the owner operates (see <see cref="GatewayOptions"/>). Used only when
    /// <see cref="CoachingOptions.ApprovedForUserContent"/> is true and the gateway is configured.
    /// </summary>
    Gateway,
}

public sealed class CoachingOptions
{
    public const string SectionName = "Coaching";

    /// <summary>Defaults to Disabled so no environment sends text anywhere by accident.</summary>
    public CoachProvider Provider { get; set; } = CoachProvider.Disabled;

    /// <summary>
    /// Owner confirmation that the live provider's data processing terms (retention, training,
    /// region) have been reviewed. Without it the live adapter is never called.
    /// </summary>
    public bool ApprovedForUserContent { get; set; }

    public int MaxInputCharacters { get; set; } = 4_000;

    public int MaxOutputTokens { get; set; } = 800;

    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(15);

    public TimeSpan OverallTimeout { get; set; } = TimeSpan.FromSeconds(40);

    /// <summary>Total provider calls per request, including retries after transient failures.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Retries allowed after output fails validation (counted within <see cref="MaxAttempts"/>).</summary>
    public int MaxInvalidOutputRetries { get; set; } = 1;

    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    public int PerUserDailyRequests { get; set; } = 30;

    public int GlobalDailyRequests { get; set; } = 2_000;

    public long GlobalDailyTokens { get; set; } = 2_000_000;

    public GatewayOptions Gateway { get; set; } = new();
}

/// <summary>
/// Settings for the owner-operated model gateway. The API key comes from the environment or a
/// secret store (Coaching__Gateway__ApiKey) and is never committed.
/// </summary>
public sealed class GatewayOptions
{
    public Uri? Endpoint { get; set; }

    public string? Model { get; set; }

    public string? ApiKey { get; set; }

    public bool IsConfigured =>
        Endpoint is { Scheme: "https" } && !string.IsNullOrWhiteSpace(Model) && !string.IsNullOrWhiteSpace(ApiKey);
}
