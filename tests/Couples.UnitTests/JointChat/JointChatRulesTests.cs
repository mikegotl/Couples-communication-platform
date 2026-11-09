using Couples.Domain.Common;
using Couples.Domain.JointChat;

namespace Couples.UnitTests.JointChat;

/// <summary>Milestone 12: the joint chat session rules, independent of storage.</summary>
public sealed class JointChatRulesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid A = Guid.CreateVersion7();
    private static readonly Guid B = Guid.CreateVersion7();

    private static JointChatSession Active()
    {
        var session = JointChatSession.Invite(Guid.CreateVersion7(), A, B, JointChatPolicy.Version, T0).Value;
        Assert.True(session.Accept(B, JointChatPolicy.Version, T0).Succeeded);
        return session;
    }

    [Fact]
    public void ConsentMustBeToTheCurrentPolicy()
    {
        Assert.Equal(DomainError.ConsentMismatch, JointChatSession.Invite(Guid.NewGuid(), A, B, "older-version", T0).Error);
        var session = JointChatSession.Invite(Guid.NewGuid(), A, B, JointChatPolicy.Version, T0).Value;
        Assert.Equal(DomainError.ConsentMismatch, session.Accept(B, "older-version", T0).Error);
        Assert.Equal(DomainError.Conflict, session.Accept(A, JointChatPolicy.Version, T0).Error);
    }

    [Fact]
    public void PartnersTakeTurnsStartingWithTheInviter()
    {
        var session = Active();
        Assert.True(session.CanWrite(A, T0));
        Assert.False(session.CanWrite(B, T0));
        Assert.True(session.RecordMessage(A, T0).Succeeded);
        Assert.Equal(DomainError.Conflict, session.RecordMessage(A, T0.AddSeconds(5)).Error);
        Assert.True(session.RecordMessage(B, T0.AddSeconds(10)).Succeeded);
        Assert.True(session.CanWrite(A, T0.AddSeconds(11)));
    }

    [Fact]
    public void AnUnansweredTurnOpensForEitherPartner()
    {
        var session = Active();
        session.RecordMessage(A, T0);
        var later = T0 + JointChatPolicy.TurnTimeout;
        Assert.True(session.ApplyTimeRules(later));
        Assert.True(session.CanWrite(A, later));
        Assert.True(session.CanWrite(B, later));
    }

    [Fact]
    public void APauseStopsEveryoneAndOnlyThePauserCanEndItEarly()
    {
        var session = Active();
        Assert.Equal(DomainError.Validation, session.Pause(B, 7, T0).Error);
        Assert.True(session.Pause(B, 5, T0).Succeeded);
        Assert.False(session.CanWrite(A, T0.AddMinutes(1)));
        Assert.Equal(DomainError.Conflict, session.Resume(A, T0.AddMinutes(1)).Error);
        Assert.True(session.Resume(B, T0.AddMinutes(1)).Succeeded);
        Assert.True(session.CanWrite(A, T0.AddMinutes(1)));
    }

    [Fact]
    public void APauseRunsOutByItselfAndDoesNotCountAsIdleTime()
    {
        var session = Active();
        session.Pause(A, 20, T0.AddMinutes(1));
        // 20 minutes of break plus 19 quiet minutes is not idle: the break restarted the clock.
        Assert.True(session.ApplyTimeRules(T0.AddMinutes(40)));
        Assert.Equal(JointChatStatus.Active, session.Status);
        Assert.False(session.IsPaused(T0.AddMinutes(40)));
    }

    [Fact]
    public void QuietChatsEndAndEveryChatHasATimeLimit()
    {
        var idle = Active();
        idle.ApplyTimeRules(T0 + JointChatPolicy.IdleTimeout);
        Assert.Equal(JointChatEndReason.Idle, idle.EndReason);

        var busy = Active();
        var now = T0;
        var writer = A;
        while (now - T0 < JointChatPolicy.MaxDuration)
        {
            now = now.AddMinutes(4);
            busy.ApplyTimeRules(now);
            if (busy.Status == JointChatStatus.Ended)
            {
                break;
            }

            busy.RecordMessage(writer, now);
            writer = writer == A ? B : A;
        }

        Assert.Equal(JointChatEndReason.TimeLimit, busy.EndReason);
    }

    [Fact]
    public void InvitationsLapse()
    {
        var session = JointChatSession.Invite(Guid.NewGuid(), A, B, JointChatPolicy.Version, T0).Value;
        Assert.False(session.ApplyTimeRules(T0 + JointChatPolicy.InvitationExpiry - TimeSpan.FromSeconds(1)));
        Assert.True(session.ApplyTimeRules(T0 + JointChatPolicy.InvitationExpiry));
        Assert.Equal(JointChatEndReason.InvitationExpired, session.EndReason);
    }

    [Fact]
    public void EitherPartnerCanLeaveAtAnyTimeEvenDuringABreak()
    {
        var session = Active();
        session.Pause(A, 20, T0);
        Assert.True(session.Leave(B, T0.AddMinutes(1)).Succeeded);
        Assert.Equal(JointChatStatus.Ended, session.Status);
        Assert.Equal(B, session.EndedByUserId);
        Assert.False(session.CanWrite(A, T0.AddMinutes(2)));
        Assert.Equal(DomainError.Conflict, session.Leave(Guid.NewGuid(), T0).Error);
    }

    [Theory]
    [InlineData("I felt hurt when the plans changed. Could we talk about Sunday?", ModerationOutcome.Allowed)]
    [InlineData("That was a stupid thing to do", ModerationOutcome.HeldToRephrase)]
    [InlineData("You NEVER listen", ModerationOutcome.HeldToRephrase)]
    [InlineData("Shut up about it", ModerationOutcome.HeldToRephrase)]
    [InlineData("The idiomatic way is fine", ModerationOutcome.Allowed)]
    [InlineData("Sometimes I want to die", ModerationOutcome.EndedForSafety)]
    [InlineData("If you do that again I'm going to kill you", ModerationOutcome.EndedForSafety)]
    public void ModerationHoldsContemptAndStopsForRisk(string text, ModerationOutcome expected) =>
        Assert.Equal(expected, JointChatModeration.Check(text));

    [Fact]
    public void MessagesAreTrimmedAndLimited()
    {
        Assert.Equal(DomainError.Validation, JointChatMessage.Create(Guid.NewGuid(), A, "   ", T0).Error);
        Assert.Equal(DomainError.Validation, JointChatMessage.Create(Guid.NewGuid(), A, new string('x', JointChatPolicy.MaxMessageLength + 1), T0).Error);
        Assert.Equal("Hi", JointChatMessage.Create(Guid.NewGuid(), A, "  Hi  ", T0).Value.Body);
    }
}
