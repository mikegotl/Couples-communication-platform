namespace Couples.Web.Safety;

/// <summary>
/// Support resources and the quick-exit destination. Resources depend on the launch
/// jurisdiction (an open owner decision) and must be reviewed by a clinician before launch; see
/// docs/safety/clinical-review.md.
/// </summary>
public sealed class SafetyOptions
{
    public const string SectionName = "Safety";

    /// <summary>A neutral page opened by "Leave quickly". Must be https.</summary>
    public Uri ExitUrl { get; set; } = new("https://www.bbc.co.uk/weather");

    /// <summary>Jurisdiction-specific services (domestic abuse, crisis lines). Added to the built-in ones below.</summary>
    public IList<SupportResource> Resources { get; } = [];

    /// <summary>Shown everywhere, whatever is configured.</summary>
    public static IReadOnlyList<SupportResource> BuiltIn { get; } =
    [
        new()
        {
            Name = "Emergency services",
            Description = "If you or anyone else is in immediate danger, call your local emergency number.",
        },
        new()
        {
            Name = "Find A Helpline",
            Description = "A free directory of crisis, suicide and domestic abuse helplines in many countries.",
            Url = new Uri("https://findahelpline.com"),
        },
    ];
}

public sealed class SupportResource
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string? Contact { get; set; }

    public Uri? Url { get; set; }
}
