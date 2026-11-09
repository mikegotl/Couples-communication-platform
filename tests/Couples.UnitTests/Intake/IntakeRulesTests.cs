using Couples.Domain.Common;
using Couples.Domain.Intake;

namespace Couples.UnitTests.Intake;

public sealed class IntakeRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly IntakeProfile profile = IntakeProfile.Start(Guid.NewGuid(), Now).Value;

    [Fact]
    public void CatalogCoversThePlaybookTopicsWithUniqueKeys()
    {
        var keys = ConcernCategory.All.Select(c => c.Key).ToList();

        Assert.Equal(["finances", "trust", "infidelity", "parenting", "conflict", "intimacy", "custom"], keys);
        Assert.All(ConcernCategory.All, c => Assert.InRange(c.Key.Length, 1, ConcernCategory.MaxKeyLength));
        Assert.Single(ConcernCategory.All, c => c.AllowsCustomLabel);
    }

    [Theory]
    [InlineData("infidelity")]
    [InlineData("intimacy")]
    [InlineData("conflict")]
    public void SensitiveTopicsNeedTheContentWarningAcknowledged(string key)
    {
        var category = ConcernCategory.Find(key)!;
        Assert.NotNull(category.ContentWarning);

        Assert.Equal(DomainError.WarningNotAcknowledged, profile.AddConcern(category, null, warningAcknowledged: false, Now).Error);
        var added = profile.AddConcern(category, null, warningAcknowledged: true, Now);

        Assert.True(added.Succeeded);
        Assert.Equal(Now, added.Value.WarningAcknowledgedAt);
    }

    [Fact]
    public void CatalogTopicCanBeChosenOnce()
    {
        Assert.True(profile.AddConcern(ConcernCategory.Finances, null, false, Now).Succeeded);
        Assert.Equal(DomainError.Conflict, profile.AddConcern(ConcernCategory.Finances, null, false, Now).Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void CustomTopicNeedsALabel(string? label) =>
        Assert.Equal(DomainError.Validation, profile.AddConcern(ConcernCategory.Custom, label, false, Now).Error);

    [Fact]
    public void CustomTopicsAreLimitedAndDistinct()
    {
        Assert.True(profile.AddConcern(ConcernCategory.Custom, "Chores", false, Now).Succeeded);
        Assert.Equal(DomainError.Conflict, profile.AddConcern(ConcernCategory.Custom, "chores", false, Now).Error);
        Assert.Equal(DomainError.Validation, profile.AddConcern(ConcernCategory.Custom, new string('x', ConcernSelection.MaxCustomLabelLength + 1), false, Now).Error);

        for (var i = 1; i < IntakeProfile.MaxCustomTopics; i++)
        {
            Assert.True(profile.AddConcern(ConcernCategory.Custom, $"Topic {i}", false, Now).Succeeded);
        }

        Assert.Equal(DomainError.Conflict, profile.AddConcern(ConcernCategory.Custom, "One too many", false, Now).Error);
    }

    [Fact]
    public void CompletingNeedsAtLeastOneTopicAndSummariesStayEditable()
    {
        Assert.Equal(DomainError.Validation, profile.Complete(Now).Error);

        var trust = profile.AddConcern(ConcernCategory.Trust, null, false, Now).Value;
        Assert.True(profile.Complete(Now).Succeeded);
        Assert.Equal(IntakeStatus.Completed, profile.Status);

        Assert.True(profile.UpdateSummary(trust.Id, "  Rebuilding after a hard year.  ", Now).Succeeded);
        Assert.Equal("Rebuilding after a hard year.", trust.Summary);
        Assert.Equal(DomainError.Validation, profile.UpdateSummary(trust.Id, new string('x', ConcernSelection.MaxSummaryLength + 1), Now).Error);
        Assert.Equal(DomainError.NotFound, profile.UpdateSummary(Guid.NewGuid(), "x", Now).Error);
    }

    [Fact]
    public void SkippingCanBeResumedButNotAfterCompleting()
    {
        Assert.True(profile.Skip(Now).Succeeded);
        Assert.Equal(IntakeStatus.Skipped, profile.Status);

        profile.AddConcern(ConcernCategory.Parenting, null, false, Now);
        Assert.Equal(IntakeStatus.InProgress, profile.Status);
        Assert.Null(profile.SkippedAt);

        profile.Complete(Now);
        Assert.Equal(DomainError.Conflict, profile.Skip(Now).Error);
    }

    [Fact]
    public void RemovingTheLastTopicReopensACompletedIntake()
    {
        var topic = profile.AddConcern(ConcernCategory.Finances, null, false, Now).Value;
        profile.Complete(Now);

        Assert.True(profile.RemoveConcern(topic.Id, Now).Succeeded);
        Assert.Equal(IntakeStatus.InProgress, profile.Status);
        Assert.Null(profile.CompletedAt);
    }
}
