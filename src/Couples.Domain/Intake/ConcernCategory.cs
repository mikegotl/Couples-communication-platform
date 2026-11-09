using Couples.Domain.Privacy;

namespace Couples.Domain.Intake;

/// <summary>
/// A topic a partner can choose to work on. Reference data: the catalog below is seeded into
/// the database so selections have a foreign key, and the app reads labels from here.
/// </summary>
[Privacy(DataClassification.Internal, "Reference data; kept while the product offers the topic.")]
public sealed class ConcernCategory
{
    public const int MaxKeyLength = 40;

    public static readonly ConcernCategory Finances = new(
        "finances", "Money and finances", 10,
        "Spending, saving, debt, income, and how you make money decisions together.");

    public static readonly ConcernCategory Trust = new(
        "trust", "Trust", 20,
        "Feeling able to rely on each other: honesty, keeping promises and following through.");

    public static readonly ConcernCategory Infidelity = new(
        "infidelity", "Infidelity or betrayal", 30,
        "An affair, a secret relationship, or another breach of what you agreed.",
        "This topic can bring up intense feelings. Go at your own pace and take a break whenever you need. What you write here stays private to you.");

    public static readonly ConcernCategory Parenting = new(
        "parenting", "Parenting and family", 40,
        "Raising children, step-family life, wider family, and sharing the load at home.");

    public static readonly ConcernCategory Conflict = new(
        "conflict", "Conflict and arguments", 50,
        "How disagreements start, how they escalate, and how you recover from them.",
        "If arguments involve threats, fear for your safety, or anyone being hurt, this app is not the right place to work through them. Please contact local emergency services or a domestic abuse support service.");

    public static readonly ConcernCategory Intimacy = new(
        "intimacy", "Intimacy and closeness", 60,
        "Emotional and physical closeness, affection and your sexual relationship.",
        "This topic includes your sexual relationship. Write only what you are comfortable with. What you write here stays private to you.");

    public static readonly ConcernCategory Custom = new(
        "custom", "Something else", 100,
        "Name a topic in your own words.",
        contentWarning: null,
        allowsCustomLabel: true);

    private ConcernCategory()
    {
    }

    private ConcernCategory(string key, string name, int sortOrder, string description, string? contentWarning = null, bool allowsCustomLabel = false)
    {
        Key = key;
        Name = name;
        SortOrder = sortOrder;
        Description = description;
        ContentWarning = contentWarning;
        AllowsCustomLabel = allowsCustomLabel;
    }

    /// <summary>Every category in display order.</summary>
    public static IReadOnlyList<ConcernCategory> All { get; } = [Finances, Trust, Infidelity, Parenting, Conflict, Intimacy, Custom];

    public string Key { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    /// <summary>Shown before the topic is added; the user must acknowledge it.</summary>
    public string? ContentWarning { get; private set; }

    public bool AllowsCustomLabel { get; private set; }

    public int SortOrder { get; private set; }

    public static ConcernCategory? Find(string? key) =>
        All.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.Ordinal));
}
