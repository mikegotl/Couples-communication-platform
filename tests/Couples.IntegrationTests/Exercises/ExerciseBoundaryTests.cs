using System.Text.Json;
using Couples.Application.Exercises;
using Couples.Application.Privacy;
using Couples.Application.Workspaces;
using Couples.Domain.Auditing;
using Couples.Domain.Common;
using Couples.Domain.Exercises;
using Couples.Infrastructure.Persistence;
using Couples.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Couples.IntegrationTests.Exercises;

/// <summary>
/// Milestone 05 gate: exercise steps can be resumed and remain private; clinician review status
/// is visible in admin metadata.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class ExerciseBoundaryTests(PostgresFixture db)
{
    private const string Listening = "reflective-listening";

    [Fact]
    public async Task CatalogIsPublishedPendingClinicianReview()
    {
        var a = await db.CreateUserAsync();
        var list = await Run<IExerciseService, IReadOnlyList<ExerciseSummaryView>>(s => s.ListAsync(a));

        Assert.Equal(["reflective-listening", "feelings-and-needs", "pause-before-reply", "repair-attempt"], list.Select(e => e.Key));

        var admin = await Run<IExerciseAdminService, IReadOnlyList<ExerciseDefinitionAdminView>>(s => s.ListDefinitionsAsync());
        var catalogRows = admin.Where(d => ExerciseCatalog.All.Any(t => t.Key == d.Key)).ToList();
        Assert.Equal(4, catalogRows.Count);
        Assert.All(catalogRows, d => Assert.True(d.MatchesCatalog));
        Assert.All(catalogRows, d => Assert.Equal(ClinicianReviewStatus.PendingClinicianReview, d.ReviewStatus));
    }

    [Fact]
    public async Task ProgressIsSavedAndResumedAcrossRequests()
    {
        var a = await db.CreateUserAsync();
        var started = await Run<IExerciseService, Result<AttemptView>>(s => s.StartOrResumeAsync(a, Listening));
        var id = started.Value.Id;

        await Run<IExerciseService, Result<AttemptView>>(s => s.SaveStepAsync(a, id, "their-words", "You never ask about my day.", moveTo: 2));
        await Run<IExerciseService, Result<AttemptView>>(s => s.SaveStepAsync(a, id, "in-my-words", "Half finished thought", moveTo: null));

        // A new "session": starting the same exercise resumes the same attempt at the same step.
        var resumed = await Run<IExerciseService, Result<AttemptView>>(s => s.StartOrResumeAsync(a, Listening));
        Assert.Equal(id, resumed.Value.Id);
        Assert.Equal(2, resumed.Value.CurrentStep);
        Assert.Equal("You never ask about my day.", resumed.Value.Answers["their-words"]);
        Assert.Equal("Half finished thought", resumed.Value.Answers["in-my-words"]);

        var list = await Run<IExerciseService, IReadOnlyList<ExerciseSummaryView>>(s => s.ListAsync(a));
        Assert.Equal(id, list.Single(e => e.Key == Listening).InProgressAttemptId);
    }

    [Fact]
    public async Task CannotSkipRequiredStepsOrFinishEarly()
    {
        var a = await db.CreateUserAsync();
        var attempt = (await Run<IExerciseService, Result<AttemptView>>(s => s.StartOrResumeAsync(a, Listening))).Value;

        Assert.Equal(DomainError.Validation, (await Run<IExerciseService, Result<AttemptView>>(s => s.SaveStepAsync(a, attempt.Id, "their-words", "", moveTo: 3))).Error);
        Assert.Equal(DomainError.Validation, (await Run<IExerciseService, Result<AttemptView>>(s => s.CompleteAsync(a, attempt.Id))).Error);
        Assert.Equal(0, (await Run<IExerciseService, Result<AttemptView>>(s => s.GetAttemptAsync(a, attempt.Id))).Value.CurrentStep);
    }

    [Fact]
    public async Task CompletingStartsAFreshAttemptNextTime()
    {
        var a = await db.CreateUserAsync();
        var first = await CompleteListeningAsync(a, "first");

        var next = await Run<IExerciseService, Result<AttemptView>>(s => s.StartOrResumeAsync(a, Listening));
        Assert.NotEqual(first, next.Value.Id);
        Assert.Empty(next.Value.Answers);

        var history = await Run<IExerciseService, IReadOnlyList<AttemptSummaryView>>(s => s.ListAttemptsAsync(a));
        Assert.Contains(history, h => h.Id == first && h.Status == AttemptStatus.Completed);
        Assert.Equal(1, (await Run<IExerciseService, IReadOnlyList<ExerciseSummaryView>>(s => s.ListAsync(a))).Single(e => e.Key == Listening).CompletedCount);
    }

    [Fact]
    public async Task PartnerCannotReadChangeFinishOrDeleteAnotherPartnersAttempt()
    {
        var (a, b) = await PairAsync();
        var secret = Marker();
        var attempt = (await Run<IExerciseService, Result<AttemptView>>(s => s.StartOrResumeAsync(a, Listening))).Value;
        await Run<IExerciseService, Result<AttemptView>>(s => s.SaveStepAsync(a, attempt.Id, "their-words", secret, null));

        Assert.Equal(DomainError.NotFound, (await Run<IExerciseService, Result<AttemptView>>(s => s.GetAttemptAsync(b, attempt.Id))).Error);
        Assert.Equal(DomainError.NotFound, (await Run<IExerciseService, Result<AttemptView>>(s => s.SaveStepAsync(b, attempt.Id, "their-words", "overwritten", null))).Error);
        Assert.Equal(DomainError.NotFound, (await Run<IExerciseService, Result<AttemptView>>(s => s.CompleteAsync(b, attempt.Id))).Error);
        Assert.Equal(DomainError.NotFound, (await Run<IExerciseService, Result>(s => s.DeleteAttemptAsync(b, attempt.Id))).Error);
        Assert.Empty(await Run<IExerciseService, IReadOnlyList<AttemptSummaryView>>(s => s.ListAttemptsAsync(b)));

        var bList = await Run<IExerciseService, IReadOnlyList<ExerciseSummaryView>>(s => s.ListAsync(b));
        Assert.All(bList, e => Assert.Null(e.InProgressAttemptId));

        // B starting the same exercise gets their own attempt, not A's.
        var bAttempt = await Run<IExerciseService, Result<AttemptView>>(s => s.StartOrResumeAsync(b, Listening));
        Assert.NotEqual(attempt.Id, bAttempt.Value.Id);
        Assert.Empty(bAttempt.Value.Answers);

        var aView = await Run<IExerciseService, Result<AttemptView>>(s => s.GetAttemptAsync(a, attempt.Id));
        Assert.Equal(secret, aView.Value.Answers["their-words"]);
    }

    [Fact]
    public async Task ExportLogsAndDeletionKeepAnswersPrivate()
    {
        var (a, b) = await PairAsync();
        var logs = new CapturingLoggerProvider();
        var aSecret = Marker();
        var attempt = (await Run<IExerciseService, Result<AttemptView>>(s => s.StartOrResumeAsync(a, Listening), logs)).Value;
        await Run<IExerciseService, Result<AttemptView>>(s => s.SaveStepAsync(a, attempt.Id, "their-words", aSecret, null), logs);

        var aExport = await Run<IPersonalDataService, PersonalDataExport>(s => s.ExportAsync(a), logs);
        var bExport = JsonSerializer.Serialize(await Run<IPersonalDataService, PersonalDataExport>(s => s.ExportAsync(b), logs));

        Assert.Equal(aSecret, aExport.ExerciseAttempts.Single().Answers["their-words"]);
        Assert.DoesNotContain(aSecret, bExport, StringComparison.Ordinal);
        Assert.DoesNotContain(logs.Lines, l => l.Contains(aSecret, StringComparison.Ordinal));

        await Run<IPersonalDataService, Guid>(s => s.RequestDeletionAsync(a));
        await Run<IPersonalDataService, int>(s => s.ProcessPendingDeletionsAsync());

        await using var provider = db.BuildServices();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CouplesDbContext>();
        Assert.False(await context.ExerciseAttempts.AnyAsync(x => x.OwnerUserId == a));
        Assert.False(await context.ExerciseResponses.AnyAsync(x => x.OwnerUserId == a));
    }

    [Fact]
    public async Task OwnerCanDeleteAnAttempt()
    {
        var a = await db.CreateUserAsync();
        var id = await CompleteListeningAsync(a, Marker());

        Assert.True((await Run<IExerciseService, Result>(s => s.DeleteAttemptAsync(a, id))).Succeeded);
        Assert.Equal(DomainError.NotFound, (await Run<IExerciseService, Result<AttemptView>>(s => s.GetAttemptAsync(a, id))).Error);
    }

    [Fact]
    public async Task AttemptsStayOnTheirVersionWhenANewVersionIsPublished()
    {
        var key = $"test-{Guid.NewGuid():N}";
        await PublishAsync(Template(key, 1));
        var a = await db.CreateUserAsync();
        var v1 = await Run<IExerciseService, Result<AttemptView>>(s => s.StartOrResumeAsync(a, key));

        await PublishAsync(Template(key, 2, extraStep: true));

        var resumed = await Run<IExerciseService, Result<AttemptView>>(s => s.StartOrResumeAsync(a, key));
        Assert.Equal(v1.Value.Id, resumed.Value.Id);
        Assert.Equal(1, resumed.Value.Version);
        Assert.Single(resumed.Value.Steps);

        var b = await db.CreateUserAsync();
        var fresh = await Run<IExerciseService, Result<AttemptView>>(s => s.StartOrResumeAsync(b, key));
        Assert.Equal(2, fresh.Value.Version);
        Assert.Equal(2, fresh.Value.Steps.Count);
    }

    [Fact]
    public async Task ReviewStatusIsRecordedAuditedAndCanGateNewAttempts()
    {
        var key = $"test-{Guid.NewGuid():N}";
        var definitionId = await PublishAsync(Template(key, 1));
        var admin = await db.CreateUserAsync();
        var user = await db.CreateUserAsync();

        // With approval required, an unreviewed exercise cannot be started.
        Assert.Equal(DomainError.NotFound, (await RunWith(requireApproval: true, (IExerciseService s) => s.StartOrResumeAsync(user, key))).Error);

        Assert.Equal(DomainError.Validation, (await Run<IExerciseAdminService, Result>(s => s.RecordReviewAsync(admin, definitionId, ClinicianReviewStatus.ClinicianApproved, "", null))).Error);
        Assert.True((await Run<IExerciseAdminService, Result>(s => s.RecordReviewAsync(admin, definitionId, ClinicianReviewStatus.ClinicianApproved, "Reviewer One, LCSW", "Approved for pilot"))).Succeeded);

        var row = (await Run<IExerciseAdminService, IReadOnlyList<ExerciseDefinitionAdminView>>(s => s.ListDefinitionsAsync())).Single(d => d.Id == definitionId);
        Assert.Equal(ClinicianReviewStatus.ClinicianApproved, row.ReviewStatus);
        Assert.Equal("Reviewer One, LCSW", row.ReviewedBy);
        Assert.NotNull(row.ReviewedAt);

        Assert.True((await RunWith(requireApproval: true, (IExerciseService s) => s.StartOrResumeAsync(user, key))).Succeeded);

        await using var provider = db.BuildServices();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CouplesDbContext>();
        Assert.True(await context.AuditEvents.AnyAsync(e => e.ActorUserId == admin && e.Action == AuditActions.ExerciseReviewed && e.SubjectId == definitionId));
    }

    [Fact]
    public async Task RetiredVersionsCannotBeStartedButExistingAttemptsResume()
    {
        var key = $"test-{Guid.NewGuid():N}";
        var definitionId = await PublishAsync(Template(key, 1));
        var a = await db.CreateUserAsync();
        var b = await db.CreateUserAsync();
        var inProgress = await Run<IExerciseService, Result<AttemptView>>(s => s.StartOrResumeAsync(a, key));

        await Run<IExerciseAdminService, Result>(s => s.RecordReviewAsync(a, definitionId, ClinicianReviewStatus.Retired, "Content lead", null));

        Assert.Equal(DomainError.NotFound, (await Run<IExerciseService, Result<AttemptView>>(s => s.StartOrResumeAsync(b, key))).Error);
        Assert.Equal(inProgress.Value.Id, (await Run<IExerciseService, Result<AttemptView>>(s => s.StartOrResumeAsync(a, key))).Value.Id);
    }

    private static ExerciseTemplate Template(string key, int version, bool extraStep = false)
    {
        List<ExerciseStep> steps = [new("one", ExerciseStepKind.Text, "One", "Write something.")];
        if (extraStep)
        {
            steps.Add(new("two", ExerciseStepKind.Text, "Two", "Write more."));
        }

        return new ExerciseTemplate(key, version, "Test exercise", "For tests.", 1, steps);
    }

    private async Task<Guid> PublishAsync(ExerciseTemplate template)
    {
        await using var provider = db.BuildServices();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CouplesDbContext>();
        var definition = ExerciseDefinition.Publish(template, db.Clock.UtcNow);
        context.ExerciseDefinitions.Add(definition);
        await context.SaveChangesAsync();
        return definition.Id;
    }

    private async Task<Guid> CompleteListeningAsync(Guid user, string text)
    {
        var id = (await Run<IExerciseService, Result<AttemptView>>(s => s.StartOrResumeAsync(user, Listening))).Value.Id;
        foreach (var (step, value) in new[] { ("their-words", text), ("in-my-words", "b"), ("what-matters", "c"), ("noticed", "calmer") })
        {
            Assert.True((await Run<IExerciseService, Result<AttemptView>>(s => s.SaveStepAsync(user, id, step, value, null))).Succeeded);
        }

        Assert.True((await Run<IExerciseService, Result<AttemptView>>(s => s.CompleteAsync(user, id))).Succeeded);
        return id;
    }

    private static string Marker() => $"marker-{Guid.NewGuid():N}";

    private async Task<(Guid A, Guid B)> PairAsync()
    {
        var a = await db.CreateUserAsync();
        var b = await db.CreateUserAsync();
        var workspace = await Run<IWorkspaceService, Result<WorkspaceSummary>>(s => s.CreateAsync(a, "Ours"));
        var invite = await Run<IWorkspaceService, Result<CreatedInvitation>>(s => s.CreateInvitationAsync(a, workspace.Value.Id));
        Assert.True((await Run<IWorkspaceService, Result<WorkspaceSummary>>(s => s.AcceptInvitationAsync(b, invite.Value.Token))).Succeeded);
        return (a, b);
    }

    private async Task<Result<AttemptView>> RunWith(bool requireApproval, Func<IExerciseService, Task<Result<AttemptView>>> call)
    {
        await using var provider = db.BuildServices(configure: s => s.Configure<ExerciseOptions>(o => o.RequireClinicianApproval = requireApproval));
        await using var scope = provider.CreateAsyncScope();
        return await call(scope.ServiceProvider.GetRequiredService<IExerciseService>());
    }

    /// <summary>Runs one call in its own scope, like one web request.</summary>
    private async Task<TResult> Run<TService, TResult>(Func<TService, Task<TResult>> call, CapturingLoggerProvider? logs = null)
        where TService : notnull
    {
        await using var provider = db.BuildServices(logs: logs);
        await using var scope = provider.CreateAsyncScope();
        return await call(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
