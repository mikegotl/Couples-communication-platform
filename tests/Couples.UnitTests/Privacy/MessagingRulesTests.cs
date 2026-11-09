using Couples.Domain.Common;
using Couples.Domain.Messaging;
using Couples.Domain.Reflections;

namespace Couples.UnitTests.Privacy;

public sealed class MessagingRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid author = Guid.NewGuid();
    private readonly Guid partner = Guid.NewGuid();
    private readonly Guid workspace = Guid.NewGuid();

    [Fact]
    public void ShareCopiesExactlyTheApprovedText()
    {
        var draft = DraftMessage.Create(author, workspace, "  I miss our walks.  ", Now).Value;

        var shared = draft.Share(partner, draft.Fingerprint, Guid.NewGuid(), Now).Value;

        Assert.Equal("I miss our walks.", shared.Body);
        Assert.Equal(author, shared.SenderUserId);
        Assert.Equal(partner, shared.RecipientUserId);
        Assert.Equal(DraftStatus.Shared, draft.Status);
    }

    [Fact]
    public void ShareFailsWhenTextChangedAfterPreview()
    {
        var draft = DraftMessage.Create(author, workspace, "First", Now).Value;
        var previewed = draft.Fingerprint;
        draft.Edit("Second", Now);

        Assert.Equal(DomainError.ConsentMismatch, draft.Share(partner, previewed, Guid.NewGuid(), Now).Error);
        Assert.Equal(DraftStatus.Draft, draft.Status);
    }

    [Fact]
    public void CannotShareWithYourselfOrTwice()
    {
        var draft = DraftMessage.Create(author, workspace, "Hello", Now).Value;

        Assert.Equal(DomainError.Validation, draft.Share(author, draft.Fingerprint, Guid.NewGuid(), Now).Error);
        Assert.True(draft.Share(partner, draft.Fingerprint, Guid.NewGuid(), Now).Succeeded);
        Assert.Equal(DomainError.Conflict, draft.Share(partner, draft.Fingerprint, Guid.NewGuid(), Now).Error);
    }

    [Fact]
    public void DiscardedOrSharedDraftsCannotBeEdited()
    {
        var draft = DraftMessage.Create(author, workspace, "Hello", Now).Value;
        draft.Discard(Now);

        Assert.Equal(DomainError.Conflict, draft.Edit("changed", Now).Error);
    }

    [Fact]
    public void OnlyTheSenderCanRevokeAndRevokingErasesText()
    {
        var draft = DraftMessage.Create(author, workspace, "Take this back", Now).Value;
        var shared = draft.Share(partner, draft.Fingerprint, Guid.NewGuid(), Now).Value;

        Assert.Equal(DomainError.NotFound, shared.Revoke(partner, Now).Error);
        Assert.True(shared.Revoke(author, Now).Succeeded);
        Assert.True(shared.IsRevoked);
        Assert.Equal(string.Empty, shared.Body);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    public void ReflectionNeedsATitleOrBody(string title, string body) =>
        Assert.Equal(DomainError.Validation, PrivateReflection.Create(author, title, body, Now).Error);

    [Fact]
    public void ReflectionBodyLengthIsLimited() =>
        Assert.Equal(DomainError.Validation, PrivateReflection.Create(author, "t", new string('x', PrivateReflection.MaxBodyLength + 1), Now).Error);
}
