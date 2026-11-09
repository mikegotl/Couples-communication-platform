using Couples.Domain.Common;
using Couples.Domain.Workspaces;

namespace Couples.UnitTests.Workspaces;

public sealed class RelationshipWorkspaceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(72);
    private readonly Guid a = Guid.NewGuid();
    private readonly Guid b = Guid.NewGuid();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NameIsRequired(string? name) =>
        Assert.Equal(DomainError.Validation, RelationshipWorkspace.Create(a, name, Now).Error);

    [Fact]
    public void NameLengthIsLimited() =>
        Assert.Equal(DomainError.Validation, RelationshipWorkspace.Create(a, new string('x', 81), Now).Error);

    [Fact]
    public void InvitationExpiresAtConfiguredLifetime()
    {
        var workspace = RelationshipWorkspace.Create(a, "Ours", Now).Value;

        var (invitation, _) = workspace.CreateInvitation(a, Now, Lifetime).Value;

        Assert.True(invitation.IsPending(Now.Add(Lifetime).AddTicks(-1)));
        Assert.False(invitation.IsPending(Now.Add(Lifetime)));
    }

    [Fact]
    public void NonMemberCannotIssueInvitation()
    {
        var workspace = RelationshipWorkspace.Create(a, "Ours", Now).Value;

        Assert.Equal(DomainError.NotFound, workspace.CreateInvitation(b, Now, Lifetime).Error);
    }

    [Fact]
    public void AcceptedInvitationCannotBeAcceptedAgain()
    {
        var workspace = RelationshipWorkspace.Create(a, "Ours", Now).Value;
        var (invitation, _) = workspace.CreateInvitation(a, Now, Lifetime).Value;
        Assert.True(workspace.Accept(invitation, b, Now).Succeeded);

        Assert.Equal(DomainError.InvitationInvalid, workspace.Accept(invitation, Guid.NewGuid(), Now).Error);
    }

    [Fact]
    public void InvitationFromAnotherWorkspaceIsRejected()
    {
        var mine = RelationshipWorkspace.Create(a, "Mine", Now).Value;
        var theirs = RelationshipWorkspace.Create(b, "Theirs", Now).Value;
        var (invitation, _) = theirs.CreateInvitation(b, Now, Lifetime).Value;

        Assert.Equal(DomainError.InvitationInvalid, mine.Accept(invitation, Guid.NewGuid(), Now).Error);
    }

    [Fact]
    public void TokensAreRandomAndOnlyTheHashIsKept()
    {
        var workspace = RelationshipWorkspace.Create(a, "Ours", Now).Value;
        var (first, firstToken) = workspace.CreateInvitation(a, Now, Lifetime).Value;
        var (_, secondToken) = workspace.CreateInvitation(a, Now, Lifetime).Value;

        Assert.NotEqual(firstToken, secondToken);
        Assert.Equal(InvitationToken.Hash(firstToken), first.TokenHash);
        Assert.DoesNotContain(firstToken, first.TokenHash, StringComparison.Ordinal);
        Assert.True(InvitationToken.LooksValid(firstToken));
    }

    [Fact]
    public void LeaveEndsMembership()
    {
        var workspace = RelationshipWorkspace.Create(a, "Ours", Now).Value;

        Assert.True(workspace.Leave(a, Now).Succeeded);
        Assert.False(workspace.IsActiveMember(a));
        Assert.Equal(DomainError.NotFound, workspace.Leave(a, Now).Error);
    }
}
