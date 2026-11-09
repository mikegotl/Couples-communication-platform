using Couples.Domain.JointChat;
using Couples.Domain.Workspaces;
using Couples.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Couples.Infrastructure.Persistence.Configurations;

internal sealed class JointChatEligibilityConfiguration : IEntityTypeConfiguration<JointChatEligibility>
{
    public void Configure(EntityTypeBuilder<JointChatEligibility> builder)
    {
        builder.ToTable("joint_chat_eligibility");
        builder.HasKey(e => e.WorkspaceId);
        builder.Property(e => e.Reference).HasMaxLength(JointChatEligibility.ReferenceMaxLength).IsRequired();
        builder.Ignore(e => e.IsActive);
        builder.Property<uint>("xmin").IsRowVersion();
        builder.HasOne<RelationshipWorkspace>().WithOne().HasForeignKey<JointChatEligibility>(e => e.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class JointChatSessionConfiguration : IEntityTypeConfiguration<JointChatSession>
{
    public void Configure(EntityTypeBuilder<JointChatSession> builder)
    {
        builder.ToTable("joint_chat_sessions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.PolicyVersion).HasMaxLength(64);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(s => s.EndReason).HasConversion<string>().HasMaxLength(24);
        builder.Property<uint>("xmin").IsRowVersion();

        // At most one chat that hasn't ended per workspace.
        builder.HasIndex(s => s.WorkspaceId).IsUnique().HasFilter("\"Status\" <> 'Ended'").HasDatabaseName("ix_joint_chat_sessions_open_per_workspace");
        builder.HasIndex(s => new { s.WorkspaceId, s.EndedAt });
        builder.HasIndex(s => s.InitiatorUserId);
        builder.HasIndex(s => s.InviteeUserId);
        builder.HasOne<RelationshipWorkspace>().WithMany().HasForeignKey(s => s.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(s => s.InitiatorUserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(s => s.InviteeUserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class JointChatMessageConfiguration : IEntityTypeConfiguration<JointChatMessage>
{
    public void Configure(EntityTypeBuilder<JointChatMessage> builder)
    {
        builder.ToTable("joint_chat_messages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Body).HasMaxLength(JointChatPolicy.MaxMessageLength).IsRequired();
        builder.HasIndex(m => new { m.SessionId, m.SentAt });
        builder.HasOne<JointChatSession>().WithMany().HasForeignKey(m => m.SessionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(m => m.AuthorUserId).OnDelete(DeleteBehavior.Cascade);
    }
}
