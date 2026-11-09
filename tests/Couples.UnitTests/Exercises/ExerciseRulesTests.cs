using Couples.Domain.Common;
using Couples.Domain.Exercises;

namespace Couples.UnitTests.Exercises;

public sealed class ExerciseRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Published content is immutable. If this fails you edited a published exercise: restore it
    /// and add a new version (with its own hash here) instead.
    /// </summary>
    public static TheoryData<string, int, string> PublishedHashes => new()
    {
        { "reflective-listening", 1, "9ff161aa5ccafc1da394c1a78bfede9fc097c2cb1b0827284f794822b8917a4c" },
        { "feelings-and-needs", 1, "ecbc52586d0cb9b78a5d19180d2d878e457f3453b4685b7b39f425cc47d0454c" },
        { "pause-before-reply", 1, "015a3db3fd0c3443bc8a4bc2fd46b3a97ba825e80fe21166209b9cb8909064aa" },
        { "repair-attempt", 1, "65a8ff4fdfdc27b5e95234dac2e5a31988f5cea239ce650ab964bc736a6ecb30" },
    };

    [Theory]
    [MemberData(nameof(PublishedHashes))]
    public void PublishedVersionsAreUnchanged(string key, int version, string hash)
    {
        var template = ExerciseCatalog.All.Single(t => t.Key == key && t.Version == version);
        Assert.Equal(hash, ExerciseDefinition.HashOf(template));
    }

    [Fact]
    public void CatalogIsWellFormed()
    {
        Assert.Equal(ExerciseCatalog.All.Count, ExerciseCatalog.All.Select(t => (t.Key, t.Version)).Distinct().Count());
        Assert.Equal(
            ["reflective-listening", "feelings-and-needs", "pause-before-reply", "repair-attempt"],
            ExerciseCatalog.All.Select(t => t.Key).Distinct());

        foreach (var template in ExerciseCatalog.All)
        {
            Assert.InRange(template.Key.Length, 1, ExerciseDefinition.MaxKeyLength);
            Assert.NotEmpty(template.Steps);
            Assert.Equal(template.Steps.Count, template.Steps.Select(s => s.Key).Distinct().Count());
            Assert.Contains(template.Steps, s => s.Kind == ExerciseStepKind.Text);
            foreach (var step in template.Steps)
            {
                Assert.InRange(step.MaxLength, 1, ExerciseStep.DefaultMaxLength);
                var isChoice = step.Kind is ExerciseStepKind.Choice or ExerciseStepKind.MultiChoice;
                Assert.Equal(isChoice, step.Choices.Count > 0);
                Assert.Equal(step.Choices.Count, step.Choices.Select(o => o.Key).Distinct().Count());
                Assert.All(step.Choices, o => Assert.DoesNotContain('\n', o.Key));
            }
        }
    }

    [Fact]
    public void CatalogAvoidsClinicalAndDiagnosticLanguage()
    {
        string[] banned = ["diagnos", "disorder", "patient", "treatment", "therapy", "therapist", "score", "symptom"];
        var text = string.Join(' ', ExerciseCatalog.All.SelectMany(t =>
            new[] { t.Title, t.Summary }.Concat(t.Steps.SelectMany(s => new[] { s.Title, s.Prompt, s.Help ?? string.Empty }.Concat(s.Choices.Select(o => o.Label))))));

        Assert.All(banned, word => Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RequiredStepsMustBeAnsweredToMoveOnOrFinish()
    {
        var (definition, attempt) = Start(ExerciseCatalog.ReflectiveListening);

        Assert.True(attempt.MoveTo(definition, 1, Now).Succeeded); // the intro needs no answer
        Assert.Equal(DomainError.Validation, attempt.MoveTo(definition, 2, Now).Error);
        Assert.Equal(DomainError.Validation, attempt.Complete(definition, Now).Error);

        Assert.True(attempt.SaveAnswer(definition, "their-words", "  We never plan anything.  ", Now).Succeeded);
        Assert.Equal("We never plan anything.", attempt.AnswerFor("their-words"));
        Assert.True(attempt.MoveTo(definition, 2, Now).Succeeded);
        Assert.True(attempt.MoveTo(definition, 0, Now).Succeeded); // back is always allowed
        Assert.Equal(0, attempt.CurrentStep);
    }

    [Fact]
    public void OptionalStepsCanBeSkipped()
    {
        var (definition, attempt) = Start(ExerciseCatalog.ReflectiveListening);
        attempt.SaveAnswer(definition, "their-words", "a", Now);
        attempt.SaveAnswer(definition, "in-my-words", "b", Now);
        attempt.SaveAnswer(definition, "what-matters", "c", Now);
        attempt.SaveAnswer(definition, "noticed", "calmer", Now);

        Assert.True(attempt.Complete(definition, Now).Succeeded);
        Assert.Equal(AttemptStatus.Completed, attempt.Status);
        Assert.Null(attempt.AnswerFor("check-question"));
    }

    [Fact]
    public void ChoiceAnswersMustBeOptionsAndKeepAuthoredOrder()
    {
        var (definition, attempt) = Start(ExerciseCatalog.PauseBeforeReply);

        Assert.Equal(DomainError.Validation, attempt.SaveAnswer(definition, "intensity", "furious", Now).Error);
        Assert.Equal(DomainError.Validation, attempt.SaveAnswer(definition, "intensity", "calm\nvery", Now).Error);
        Assert.True(attempt.SaveAnswer(definition, "intensity", "very", Now).Succeeded);

        var (needsDefinition, needsAttempt) = Start(ExerciseCatalog.FeelingsAndNeeds);
        Assert.True(needsAttempt.SaveAnswer(needsDefinition, "needs", "rest\nheard\nrest", Now).Succeeded);
        Assert.Equal("heard\nrest", needsAttempt.AnswerFor("needs"));
    }

    [Fact]
    public void InfoStepsUnknownStepsAndOverlongTextAreRejected()
    {
        var (definition, attempt) = Start(ExerciseCatalog.FeelingsAndNeeds);

        Assert.Equal(DomainError.Validation, attempt.SaveAnswer(definition, "intro", "x", Now).Error);
        Assert.Equal(DomainError.Validation, attempt.SaveAnswer(definition, "nope", "x", Now).Error);
        Assert.Equal(DomainError.Validation, attempt.SaveAnswer(definition, "feelings-other", new string('x', 501), Now).Error);
    }

    [Fact]
    public void ClearingAnAnswerRemovesIt()
    {
        var (definition, attempt) = Start(ExerciseCatalog.RepairAttempt);
        attempt.SaveAnswer(definition, "what-happened", "We argued about dinner.", Now);

        Assert.True(attempt.SaveAnswer(definition, "what-happened", "   ", Now).Succeeded);
        Assert.Null(attempt.AnswerFor("what-happened"));
        Assert.Empty(attempt.Responses);
    }

    [Fact]
    public void AnswersAreValidatedAgainstTheAttemptsOwnVersion()
    {
        var (_, attempt) = Start(ExerciseCatalog.RepairAttempt);
        var other = ExerciseDefinition.Publish(ExerciseCatalog.RepairAttempt, Now);

        Assert.Equal(DomainError.Validation, attempt.SaveAnswer(other, "what-happened", "x", Now).Error);
    }

    [Fact]
    public void ReviewNeedsAReviewerName()
    {
        var definition = ExerciseDefinition.Publish(ExerciseCatalog.RepairAttempt, Now);
        Assert.Equal(ClinicianReviewStatus.PendingClinicianReview, definition.ReviewStatus);

        Assert.Equal(DomainError.Validation, definition.RecordReview(ClinicianReviewStatus.ClinicianApproved, " ", null, Now).Error);
        Assert.True(definition.RecordReview(ClinicianReviewStatus.ClinicianApproved, "Dr. Example, LMFT", "Fine as written.", Now).Succeeded);
        Assert.Equal("Dr. Example, LMFT", definition.ReviewedBy);
        Assert.Equal(Now, definition.ReviewedAt);
    }

    private static (ExerciseDefinition Definition, ExerciseAttempt Attempt) Start(ExerciseTemplate template)
    {
        var definition = ExerciseDefinition.Publish(template, Now);
        return (definition, ExerciseAttempt.Start(Guid.NewGuid(), definition, Now).Value);
    }
}
