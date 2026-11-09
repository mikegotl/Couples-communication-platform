using Couples.Domain.Workspaces;
using Couples.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Couples.Infrastructure.Persistence.Configurations;

internal sealed class RelationshipWorkspaceConfiguration : IEntityTypeConfiguration<RelationshipWorkspace>
{
    public void Configure(EntityTypeBuilder<RelationshipWorkspace> builder)
    {
        builder.ToTable("workspaces");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Name).HasMaxLength(RelationshipWorkspace.MaxNameLength).IsRequired();
        builder.Ignore(w => w.ActiveMemberCount);

        // Optimistic concurrency on PostgreSQL's system row version, so two members changing
        // the workspace at once (for example two people accepting invitations) cannot both win.
        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasMany(w => w.Memberships).WithOne().HasForeignKey(m => m.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(w => w.Invitations).WithOne().HasForeignKey(i => i.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(w => w.Memberships).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(w => w.Invitations).UsePropertyAccessMode(PropertyAccessMode.Field);

        // No foreign key on CreatedByUserId: the workspace (and the partner's place in it) must
        // survive the creator deleting their account. The id left behind is an unlinkable random value.
    }
}

internal sealed class WorkspaceMembershipConfiguration : IEntityTypeConfiguration<WorkspaceMembership>
{
    public void Configure(EntityTypeBuilder<WorkspaceMembership> builder)
    {
        builder.ToTable("workspace_memberships");
        builder.HasKey(m => m.Id);
        builder.Ignore(m => m.IsActive);
        builder.Property(m => m.Role).HasConversion<string>().HasMaxLength(20);

        // A user has at most one active membership (Phase 1: one relationship workspace per user).
        builder.HasIndex(m => m.UserId).IsUnique().HasFilter("\"LeftAt\" IS NULL").HasDatabaseName("ux_memberships_active_user");
        builder.HasIndex(m => new { m.WorkspaceId, m.UserId });

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.ToTable("invitations");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.TokenHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(i => i.TokenHash).IsUnique();
        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(i => i.CreatedByUserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(i => i.AcceptedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}
