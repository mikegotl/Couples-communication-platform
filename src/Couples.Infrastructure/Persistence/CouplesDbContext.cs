using Couples.Domain.Common;
using Couples.Domain.Workspaces;
using Couples.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Couples.Infrastructure.Persistence;

/// <summary>
/// Primary EF Core context: ASP.NET Core Identity tables plus domain entities, all in the
/// <c>couples</c> schema.
/// </summary>
public sealed class CouplesDbContext(DbContextOptions<CouplesDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public const string Schema = "couples";

    public DbSet<RelationshipWorkspace> Workspaces => Set<RelationshipWorkspace>();

    public DbSet<WorkspaceMembership> WorkspaceMemberships => Set<WorkspaceMembership>();

    public DbSet<Invitation> Invitations => Set<Invitation>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(Schema);

        builder.ApplyConfigurationsFromAssembly(typeof(CouplesDbContext).Assembly);

        // Domain entities create their own ids (UUIDv7). Telling EF the key is never generated
        // makes it insert children added through a navigation instead of treating them as
        // existing rows to update.
        foreach (var entityType in builder.Model.GetEntityTypes().Where(t => typeof(Entity).IsAssignableFrom(t.ClrType)))
        {
            builder.Entity(entityType.ClrType).Property(nameof(Entity.Id)).ValueGeneratedNever();
        }
    }
}
