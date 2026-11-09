using System.Text.Json;
using Couples.Application.Intake;
using Couples.Application.Privacy;
using Couples.Application.Workspaces;
using Couples.Domain.Common;
using Couples.Domain.Intake;
using Couples.Infrastructure.Persistence;
using Couples.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Couples.IntegrationTests.Intake;

/// <summary>
/// Milestone 04 gate: each partner completes intake independently and nothing about one
/// partner's intake (topics, notes, status) reaches the other.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class IntakeBoundaryTests(PostgresFixture db)
{
    [Fact]
    public async Task EachPartnerCompletesIntakeIndependently()
    {
        var (a, b) = await PairAsync();

        var aTrust = await Run<IIntakeService, Result<IntakeView>>(s => s.AddConcernAsync(a, "trust", null, false));
        await Run<IIntakeService, Result<IntakeView>>(s => s.AddConcernAsync(a, "infidelity", null, true));
        await Run<IIntakeService, Result<IntakeView>>(s => s.UpdateSummaryAsync(a, aTrust.Value.Concerns[0].Id, "A's note"));
        Assert.True((await Run<IIntakeService, Result<IntakeView>>(s => s.CompleteAsync(a))).Succeeded);

        Assert.Null(await Run<IIntakeService, IntakeView?>(s => s.GetAsync(b)));

        await Run<IIntakeService, Result<IntakeView>>(s => s.AddConcernAsync(b, "finances", null, false));
        await Run<IIntakeService, Result<IntakeView>>(s => s.AddConcernAsync(b, "custom", "Chores", false));
        Assert.True((await Run<IIntakeService, Result<IntakeView>>(s => s.CompleteAsync(b))).Succeeded);

        var aView = await Run<IIntakeService, IntakeView?>(s => s.GetAsync(a));
        var bView = await Run<IIntakeService, IntakeView?>(s => s.GetAsync(b));

        Assert.Equal(["trust", "infidelity"], aView!.Concerns.Select(c => c.CategoryKey));
        Assert.Equal("A's note", aView.Concerns[0].Summary);
        Assert.Equal(["finances", "custom"], bView!.Concerns.Select(c => c.CategoryKey));
        Assert.Equal("Chores", bView.Concerns[1].Title);
        Assert.All(bView.Concerns, c => Assert.Equal(string.Empty, c.Summary));
    }

    [Fact]
    public async Task PartnerCannotReadChangeOrRemoveAnotherPartnersTopicsById()
    {
        var (a, b) = await PairAsync();
        var secret = Marker();
        var aIntake = await Run<IIntakeService, Result<IntakeView>>(s => s.AddConcernAsync(a, "trust", null, false));
        var aTopic = aIntake.Value.Concerns[0].Id;
        await Run<IIntakeService, Result<IntakeView>>(s => s.UpdateSummaryAsync(a, aTopic, secret));

        // B without an intake, then B with their own intake: A's topic id is unknown either way.
        Assert.Equal(DomainError.NotFound, (await Run<IIntakeService, Result<IntakeView>>(s => s.UpdateSummaryAsync(b, aTopic, "overwritten"))).Error);
        await Run<IIntakeService, Result<IntakeView>>(s => s.AddConcernAsync(b, "parenting", null, false));
        Assert.Equal(DomainError.NotFound, (await Run<IIntakeService, Result<IntakeView>>(s => s.UpdateSummaryAsync(b, aTopic, "overwritten"))).Error);
        Assert.Equal(DomainError.NotFound, (await Run<IIntakeService, Result<IntakeView>>(s => s.RemoveConcernAsync(b, aTopic))).Error);

        var bView = await Run<IIntakeService, IntakeView?>(s => s.GetAsync(b));
        Assert.DoesNotContain(bView!.Concerns, c => c.Id == aTopic || c.Summary == secret);

        var aView = await Run<IIntakeService, IntakeView?>(s => s.GetAsync(a));
        Assert.Equal(secret, aView!.Concerns.Single().Summary);
    }

    [Fact]
    public async Task WorkspaceViewRevealsNothingAboutEitherPartnersIntake()
    {
        var (a, b) = await PairAsync();
        var label = Marker();
        await Run<IIntakeService, Result<IntakeView>>(s => s.AddConcernAsync(a, "custom", label, false));
        await Run<IIntakeService, Result<IntakeView>>(s => s.CompleteAsync(a));

        var bWorkspace = await Run<IWorkspaceService, WorkspaceSummary?>(s => s.GetMyWorkspaceAsync(b));
        var serialized = JsonSerializer.Serialize(bWorkspace);

        Assert.DoesNotContain(label, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("intake", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("concern", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExportIncludesOnlyTheRequestersIntake()
    {
        var (a, b) = await PairAsync();
        var aSecret = Marker();
        var bSecret = Marker();
        var aIntake = await Run<IIntakeService, Result<IntakeView>>(s => s.AddConcernAsync(a, "custom", aSecret, false));
        await Run<IIntakeService, Result<IntakeView>>(s => s.UpdateSummaryAsync(a, aIntake.Value.Concerns[0].Id, Marker()));
        await Run<IIntakeService, Result<IntakeView>>(s => s.AddConcernAsync(b, "custom", bSecret, false));

        var bExport = JsonSerializer.Serialize(await Run<IPersonalDataService, PersonalDataExport>(s => s.ExportAsync(b)));
        var aExport = await Run<IPersonalDataService, PersonalDataExport>(s => s.ExportAsync(a));

        Assert.Contains(bSecret, bExport, StringComparison.Ordinal);
        Assert.DoesNotContain(aSecret, bExport, StringComparison.Ordinal);
        Assert.Equal(aSecret, aExport.Intake!.Concerns.Single().CustomLabel);
    }

    [Fact]
    public async Task LogsNeverContainTopicsOrNotes()
    {
        var logs = new CapturingLoggerProvider();
        var a = await db.CreateUserAsync();
        var label = Marker();
        var note = Marker();

        var intake = await Run<IIntakeService, Result<IntakeView>>(s => s.AddConcernAsync(a, "custom", label, false), logs);
        await Run<IIntakeService, Result<IntakeView>>(s => s.UpdateSummaryAsync(a, intake.Value.Concerns[0].Id, note), logs);
        await Run<IIntakeService, Result<IntakeView>>(s => s.CompleteAsync(a), logs);
        await Run<IPersonalDataService, PersonalDataExport>(s => s.ExportAsync(a), logs);

        Assert.NotEmpty(logs.Lines);
        Assert.DoesNotContain(logs.Lines, m => m.Contains(label, StringComparison.Ordinal) || m.Contains(note, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SkipStartOverAndAccountDeletionEraseIntake()
    {
        var a = await db.CreateUserAsync();
        var skipped = await Run<IIntakeService, Result<IntakeView>>(s => s.SkipAsync(a));
        Assert.Equal(IntakeStatus.Skipped, skipped.Value.Status);
        Assert.Empty(skipped.Value.Concerns);

        await Run<IIntakeService, Result<IntakeView>>(s => s.AddConcernAsync(a, "finances", null, false));
        await Run<IIntakeService, bool>(async s => { await s.DeleteAsync(a); return true; });
        Assert.Null(await Run<IIntakeService, IntakeView?>(s => s.GetAsync(a)));

        await Run<IIntakeService, Result<IntakeView>>(s => s.AddConcernAsync(a, "intimacy", null, true));
        await Run<IPersonalDataService, Guid>(s => s.RequestDeletionAsync(a));
        await Run<IPersonalDataService, int>(s => s.ProcessPendingDeletionsAsync());

        await using var provider = db.BuildServices();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CouplesDbContext>();
        Assert.False(await context.IntakeProfiles.AnyAsync(p => p.OwnerUserId == a));
        Assert.False(await context.ConcernSelections.AnyAsync(c => c.OwnerUserId == a));
    }

    [Fact]
    public async Task ConcurrentFirstSelectionsCreateOneIntake()
    {
        var a = await db.CreateUserAsync();

        var results = await Task.WhenAll(
            Run<IIntakeService, Result<IntakeView>>(s => s.AddConcernAsync(a, "finances", null, false)),
            Run<IIntakeService, Result<IntakeView>>(s => s.AddConcernAsync(a, "trust", null, false)));

        Assert.Contains(results, r => r.Succeeded);
        Assert.All(results, r => Assert.True(r.Succeeded || r.Error == DomainError.Conflict));

        await using var provider = db.BuildServices();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CouplesDbContext>();
        Assert.Equal(1, await context.IntakeProfiles.CountAsync(p => p.OwnerUserId == a));
    }

    [Fact]
    public async Task UnknownCategoryAndUnacknowledgedWarningAreRejected()
    {
        var a = await db.CreateUserAsync();

        Assert.Equal(DomainError.Validation, (await Run<IIntakeService, Result<IntakeView>>(s => s.AddConcernAsync(a, "not-a-topic", null, false))).Error);
        Assert.Equal(DomainError.WarningNotAcknowledged, (await Run<IIntakeService, Result<IntakeView>>(s => s.AddConcernAsync(a, "conflict", null, false))).Error);
        Assert.Null(await Run<IIntakeService, IntakeView?>(s => s.GetAsync(a)));
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

    /// <summary>Runs one call in its own scope, like one web request.</summary>
    private async Task<TResult> Run<TService, TResult>(Func<TService, Task<TResult>> call, CapturingLoggerProvider? logs = null)
        where TService : notnull
    {
        await using var provider = db.BuildServices(logs: logs);
        await using var scope = provider.CreateAsyncScope();
        return await call(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
