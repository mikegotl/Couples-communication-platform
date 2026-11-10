using Couples.Application.Coaching;
using Couples.Application.Exercises;
using Couples.Application.Inbox;
using Couples.Application.Intake;
using Couples.Application.Messaging;
using Couples.Application.Pace;
using Couples.Application.Privacy;
using Couples.Application.Reflections;
using Couples.Application.Safety;
using Couples.Application.Workspaces;
using Couples.Domain.Common;
using Couples.Infrastructure.Persistence;
using Couples.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Couples.IntegrationTests.Privacy;

/// <summary>
/// Milestone 10 gate, "deletion verified": a user who has touched every feature is deleted,
/// then every column in the model that holds a user id is searched for them.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class DeletionSweepTests(PostgresFixture db)
{
    /// <summary>
    /// Columns that intentionally keep the id after deletion, as documented in the data inventory:
    /// audit events (ids only, unlinkable once the account is gone) and the deletion request itself
    /// (evidence that it was honoured, and used to re-erase an account that reappears after a restore).
    /// </summary>
    private static readonly HashSet<string> KeptByDesign = ["AuditEvent.ActorUserId", "DataDeletionRequest.UserId"];

    /// <summary>Identity features this product doesn't seed (external logins, claims, stored tokens, passkeys, roles).</summary>
    private static readonly HashSet<string> NotSeeded =
    [
        "IdentityUserClaim<Guid>.UserId", "IdentityUserLogin<Guid>.UserId", "IdentityUserToken<Guid>.UserId",
        "IdentityUserPasskey<Guid>.UserId", "IdentityUserRole<Guid>.UserId", "ExerciseDefinition.ReviewedByUserId",
    ];

    [Fact]
    public async Task NoUserIdSurvivesDeletionOutsideDocumentedEvidence()
    {
        var a = await db.CreateUserAsync();
        var b = await db.CreateUserAsync();
        await SeedEverythingAsync(a, b);

        var columns = UserColumns();
        var before = await CountsAsync(columns, a);
        var unseeded = before.Where(c => c.Value == 0 && !NotSeeded.Contains(c.Key) && c.Key != "DataDeletionRequest.UserId").Select(c => c.Key).ToList();
        Assert.True(unseeded.Count == 0, "Seed these so deletion is really tested: " + string.Join(", ", unseeded));

        await Run<IPersonalDataService, Guid>(s => s.RequestDeletionAsync(a));
        Assert.True(await Run<IPersonalDataService, int>(s => s.ProcessPendingDeletionsAsync()) >= 1);

        var after = await CountsAsync(columns, a);
        var left = after.Where(c => c.Value > 0 && !KeptByDesign.Contains(c.Key)).Select(c => $"{c.Key}={c.Value}").ToList();
        Assert.True(left.Count == 0, "Still holding the deleted user's id: " + string.Join(", ", left));

        // The partner keeps their own account and private data.
        var partner = await CountsAsync(columns, b);
        Assert.True(partner["ApplicationUser.Id"] == 1 && partner["PrivateReflection.OwnerUserId"] >= 1);
    }

    [Fact]
    public async Task AnAccountRestoredFromBackupIsErasedAgain()
    {
        var a = await db.CreateUserAsync();
        await Run<IPrivateReflectionService, Result<ReflectionView>>(s => s.CreateAsync(a, "Before", "Private words"));
        await Run<IPersonalDataService, Guid>(s => s.RequestDeletionAsync(a));
        await Run<IPersonalDataService, int>(s => s.ProcessPendingDeletionsAsync());

        // Simulate restoring a backup taken before the deletion: the account and its data come back.
        await using (var conn = new NpgsqlConnection(db.ConnectionString))
        {
            await conn.OpenAsync();
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO couples."AspNetUsers" ("Id", "UserName", "NormalizedUserName", "Email", "NormalizedEmail", "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
                VALUES (@id, @n, upper(@n), @n, upper(@n), true, false, false, true, 0)
                """, conn);
            insert.Parameters.AddWithValue("id", a);
            insert.Parameters.AddWithValue("n", $"{a:N}@restored.test");
            await insert.ExecuteNonQueryAsync();
        }

        await Run<IPrivateReflectionService, Result<ReflectionView>>(s => s.CreateAsync(a, "Restored", "Restored words"));

        Assert.True(await Run<IPersonalDataService, int>(s => s.ProcessPendingDeletionsAsync()) >= 1);
        var counts = await CountsAsync(UserColumns(), a);
        Assert.Equal(0, counts["ApplicationUser.Id"]);
        Assert.Equal(0, counts["PrivateReflection.OwnerUserId"]);
    }

    private async Task SeedEverythingAsync(Guid a, Guid b)
    {
        // A leaves an earlier workspace (with an invitation they created), then joins B's.
        var earlier = await Run<IWorkspaceService, Result<WorkspaceSummary>>(s => s.CreateAsync(a, "Earlier"));
        await Run<IWorkspaceService, Result<CreatedInvitation>>(s => s.CreateInvitationAsync(a, earlier.Value.Id));
        Assert.True((await Run<IWorkspaceService, Result>(s => s.LeaveAsync(a, earlier.Value.Id))).Succeeded);
        var workspace = await Run<IWorkspaceService, Result<WorkspaceSummary>>(s => s.CreateAsync(b, "Ours"));
        var invite = await Run<IWorkspaceService, Result<CreatedInvitation>>(s => s.CreateInvitationAsync(b, workspace.Value.Id));
        Assert.True((await Run<IWorkspaceService, Result<WorkspaceSummary>>(s => s.AcceptInvitationAsync(a, invite.Value.Token))).Succeeded);

        await Run<IIntakeService, Result<IntakeView>>(s => s.AddConcernAsync(a, "trust", null, false));
        var attempt = await Run<IExerciseService, Result<AttemptView>>(s => s.StartOrResumeAsync(a, "reflective-listening"));
        await Run<IExerciseService, Result<AttemptView>>(s => s.SaveStepAsync(a, attempt.Value.Id, "their-words", "Words", moveTo: 2));
        await Run<IPrivateReflectionService, Result<ReflectionView>>(s => s.CreateAsync(a, "Mine", "Private"));
        await Run<IPrivateReflectionService, Result<ReflectionView>>(s => s.CreateAsync(b, "Partner's", "Theirs"));

        var draft = await Run<IMessagingService, Result<DraftView>>(s => s.CreateDraftAsync(a, "Hello"));
        Assert.True((await Run<IMessagingService, Result<SharedMessageView>>(s => s.ShareAsync(a, draft.Value.Id, draft.Value.Fingerprint))).Succeeded);
        var reply = await Run<IMessagingService, Result<DraftView>>(s => s.CreateDraftAsync(b, "Hi back"));
        var replied = await Run<IMessagingService, Result<SharedMessageView>>(s => s.ShareAsync(b, reply.Value.Id, reply.Value.Fingerprint));
        await Run<IInboxService, Result>(s => s.SaveReflectionAsync(a, replied.Value.Id, "My notes"));
        await Run<IMessagingService, Result<DraftView>>(s => s.CreateDraftAsync(a, "Unsent"));

        await Run<IPaceService, Result<PaceSettings>>(s => s.SetMutedAsync(a, false));
        await Run<ISafetyService, SafetyStatus>(s => s.SetSafetyModeAsync(a, false));
        await Run<ICoachUsageStore, bool>(s => s.TryReserveAsync(a, new DateOnly(2026, 11, 9), 100, 100_000, 100_000_000));
        await Run<Couples.Application.Pilot.IPilotService, Result<Couples.Application.Pilot.PilotStatus>>(s => s.EnrollAsync(a, Couples.Domain.Pilot.PilotDocuments.TermsVersion, Couples.Domain.Pilot.PilotDocuments.PrivacyNoticeVersion, null));
        await Run<Couples.Application.Feedback.IFeedbackService, Result>(s => s.SubmitAsync(a, Couples.Domain.Feedback.FeedbackArea.General, 4, "Good", allowContact: true));

        // Joint chat (milestone 12): A granted eligibility (as an admin), left one chat, and is
        // invited into a second where it is A's turn and A has paused.
        await using (var provider = db.BuildServices(configure: JointChatPilot.On()))
        {
            await using var scope = provider.CreateAsyncScope();
            await JointChatPilot.MakeEligibleAsync(scope.ServiceProvider.GetRequiredService<CouplesDbContext>(), workspace.Value.Id, a, db.Clock.UtcNow);
            var chat = scope.ServiceProvider.GetRequiredService<Couples.Application.JointChat.IJointChatService>();
            var first = await chat.StartAsync(a, Couples.Domain.JointChat.JointChatPolicy.Version);
            Assert.True((await chat.JoinAsync(b, first.Value.SessionId!.Value, Couples.Domain.JointChat.JointChatPolicy.Version)).Succeeded);
            Assert.True((await chat.SendAsync(a, first.Value.SessionId!.Value, "Hello from A")).Succeeded);
            await chat.LeaveAsync(a, first.Value.SessionId!.Value);
            var second = await chat.StartAsync(b, Couples.Domain.JointChat.JointChatPolicy.Version);
            Assert.True((await chat.JoinAsync(a, second.Value.SessionId!.Value, Couples.Domain.JointChat.JointChatPolicy.Version)).Succeeded);
            Assert.True((await chat.SendAsync(b, second.Value.SessionId!.Value, "Hello from B")).Succeeded);
            Assert.True((await chat.PauseAsync(a, second.Value.SessionId!.Value, 5)).Succeeded);
        }

        // Date rewards: A cashed in the couple's credits for a voucher.
        await using (var provider = db.BuildServices())
        {
            await using var scope = provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<CouplesDbContext>();
            var offer = Couples.Domain.Rewards.RewardOffer.Create(new Couples.Domain.Rewards.RewardOfferDetails("Dinner", "Bistro", null, null, Couples.Domain.Rewards.RewardKind.Restaurant, 10, true), db.Clock.UtcNow).Value;
            context.RewardOffers.Add(offer);
            context.RewardRedemptions.Add(Couples.Domain.Rewards.RewardRedemption.Reserve(workspace.Value.Id, offer, a, "Local", db.Clock.UtcNow));
            await context.SaveChangesAsync();
        }
    }

    private static List<(string Key, string Table, string Column)> UserColumns()
    {
        using var context = new CouplesDbContext(new DbContextOptionsBuilder<CouplesDbContext>().UseNpgsql("Host=unused").Options);
        var result = new List<(string, string, string)>();
        foreach (var entity in context.Model.GetEntityTypes().Where(e => e.GetTableName() is not null && !e.IsOwned()))
        {
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            var name = entity.ClrType.Name.Replace("`1", "<Guid>", StringComparison.Ordinal);
            foreach (var property in entity.GetProperties().Where(p => p.ClrType == typeof(Guid) || p.ClrType == typeof(Guid?)))
            {
                var isUserKey = name == "ApplicationUser" && property.Name == "Id";
                if (isUserKey || property.Name.EndsWith("UserId", StringComparison.Ordinal))
                {
                    result.Add(($"{name}.{property.Name}", $"\"{entity.GetSchema()}\".\"{entity.GetTableName()}\"", $"\"{property.GetColumnName(table)}\""));
                }
            }
        }

        Assert.Contains(result, c => c.Item1 == "PersonalSafety.UserId");
        return result;
    }

    private async Task<Dictionary<string, long>> CountsAsync(List<(string Key, string Table, string Column)> columns, Guid userId)
    {
        await using var conn = new NpgsqlConnection(db.ConnectionString);
        await conn.OpenAsync();
        var counts = new Dictionary<string, long>();
        foreach (var (key, table, column) in columns)
        {
#pragma warning disable CA2100 // Identifiers come from the EF model, the value is a parameter.
            await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM {table} WHERE {column} = @id", conn);
#pragma warning restore CA2100
            cmd.Parameters.AddWithValue("id", userId);
            counts[key] = (long)(await cmd.ExecuteScalarAsync())!;
        }

        return counts;
    }

    private async Task<TResult> Run<TService, TResult>(Func<TService, Task<TResult>> call)
        where TService : notnull
    {
        await using var provider = db.BuildServices();
        await using var scope = provider.CreateAsyncScope();
        return await call(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
