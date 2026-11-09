namespace Couples.Application.Workspaces;

public sealed class WorkspaceOptions
{
    public const string SectionName = "Workspace";

    /// <summary>How long an invitation link stays valid. Default 72 hours.</summary>
    public TimeSpan InvitationLifetime { get; set; } = TimeSpan.FromHours(72);
}
