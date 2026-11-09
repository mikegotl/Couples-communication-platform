using Couples.Application;
using Couples.Application.Abstractions;
using Couples.Application.JointChat;
using Couples.Domain.JointChat;
using Couples.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Couples.IntegrationTests.Infrastructure;

/// <summary>Turns the joint chat pilot on for a test's services: flag on and both sign-offs recorded.</summary>
public static class JointChatPilot
{
    public static Action<IServiceCollection> On(bool flag = true, bool signedOff = true) => services =>
    {
        services.AddSingleton<IFeatureFlags>(new FixedFlags(flag ? [FeatureFlags.Phase2JointChat] : []));
        services.Configure<JointChatOptions>(o =>
        {
            o.ClinicalSignOff = signedOff ? "CR-TEST-1" : null;
            o.SecuritySignOff = signedOff ? "SR-TEST-1" : null;
        });
    };

    /// <summary>Marks a workspace eligible directly in the database (the admin path is tested separately).</summary>
    public static async Task MakeEligibleAsync(CouplesDbContext db, Guid workspaceId, Guid adminUserId, DateTimeOffset now)
    {
        db.JointChatEligibility.Add(JointChatEligibility.Grant(workspaceId, "P-TEST", adminUserId, now).Value);
        await db.SaveChangesAsync();
    }

    public static async Task MakeAdminAsync(CouplesDbContext db, Guid userId)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.NormalizedName == "ADMIN");
        if (role is null)
        {
            role = new IdentityRole<Guid> { Id = Guid.CreateVersion7(), Name = "Admin", NormalizedName = "ADMIN", ConcurrencyStamp = Guid.NewGuid().ToString() };
            db.Roles.Add(role);
        }

        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = userId, RoleId = role.Id });
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Another test created the role at the same time; retry with it.
            db.ChangeTracker.Clear();
            var existing = await db.Roles.FirstAsync(r => r.NormalizedName == "ADMIN");
            db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = userId, RoleId = existing.Id });
            await db.SaveChangesAsync();
        }
    }

    private sealed class FixedFlags(string[] on) : IFeatureFlags
    {
        public Task<bool> IsEnabledAsync(string flag, CancellationToken cancellationToken = default) => Task.FromResult(on.Contains(flag));
    }
}
