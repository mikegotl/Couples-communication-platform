namespace Couples.Domain.Exercises;

public enum ExerciseStepKind
{
    /// <summary>Text to read; no answer.</summary>
    Info,

    /// <summary>A free-text answer.</summary>
    Text,

    /// <summary>Exactly one option.</summary>
    Choice,

    /// <summary>Any number of options.</summary>
    MultiChoice,
}

public sealed record ExerciseOption(string Key, string Label);

/// <summary>One screen of an exercise. Keys are stable within a version so answers can be resumed.</summary>
public sealed record ExerciseStep(
    string Key,
    ExerciseStepKind Kind,
    string Title,
    string Prompt,
    string? Help = null,
    IReadOnlyList<ExerciseOption>? Options = null,
    bool Optional = false,
    int MaxLength = ExerciseStep.DefaultMaxLength)
{
    public const int DefaultMaxLength = 4_000;

    public IReadOnlyList<ExerciseOption> Choices => Options ?? [];

    public bool NeedsAnswer => Kind != ExerciseStepKind.Info && !Optional;
}

/// <summary>
/// The authored content of one version of an exercise. Published versions are immutable: any
/// change to the wording or steps needs a new version number so attempts stay consistent and
/// each version can be reviewed on its own.
/// </summary>
public sealed record ExerciseTemplate(
    string Key,
    int Version,
    string Title,
    string Summary,
    int EstimatedMinutes,
    IReadOnlyList<ExerciseStep> Steps);
