using System.Text.RegularExpressions;
using Couples.Application.Safety;
using Couples.Domain.Safety;

namespace Couples.UnitTests.Safety;

public sealed class SafetyContentTests
{
    private static Dictionary<string, string> Answers(string safe = "yes", string afraid = "no", string sayNo = "yes", string selfHarm = "no") => new()
    {
        ["safe-at-home"] = safe,
        ["afraid"] = afraid,
        ["can-say-no"] = sayNo,
        ["self-harm"] = selfHarm,
    };

    [Theory]
    [InlineData("yes", "no", "yes", SafetyLevel.NoConcerns)]
    [InlineData("unsure", "no", "yes", SafetyLevel.Concern)]
    [InlineData("yes", "skip", "yes", SafetyLevel.Concern)]
    [InlineData("yes", "no", "not-always", SafetyLevel.Concern)]
    [InlineData("no", "no", "yes", SafetyLevel.Unsafe)]
    [InlineData("yes", "sometimes", "yes", SafetyLevel.Unsafe)]
    [InlineData("yes", "yes", "yes", SafetyLevel.Unsafe)]
    [InlineData("yes", "no", "no", SafetyLevel.Unsafe)]
    [InlineData("unsure", "yes", "not-always", SafetyLevel.Unsafe)]
    public void PlaceholderRuleErrsTowardsSafety(string safe, string afraid, string sayNo, SafetyLevel expected) =>
        Assert.Equal(expected, SafetyContent.Assess(Answers(safe, afraid, sayNo)).Level);

    [Theory]
    [InlineData("no", false)]
    [InlineData("yes", true)]
    [InlineData("skip", true)]
    public void SelfHarmSupportIsShownWhateverTheLevel(string answer, bool expected) =>
        Assert.Equal(expected, SafetyContent.Assess(Answers(selfHarm: answer)).SelfHarmSupport);

    [Fact]
    public void RequiresAKnownAnswerForEveryQuestion()
    {
        Assert.True(SafetyContent.IsComplete(Answers()));
        var missing = Answers();
        missing.Remove("self-harm");
        Assert.False(SafetyContent.IsComplete(missing));
        Assert.False(SafetyContent.IsComplete(Answers(safe: "maybe")));
        var extra = Answers();
        extra["other"] = "x";
        Assert.False(SafetyContent.IsComplete(extra));
    }

    [Fact]
    public void RecordingAnIncompleteCheckFails() =>
        Assert.False(PersonalSafety.For(Guid.NewGuid()).RecordCheck(new Dictionary<string, string>(), DateTimeOffset.UnixEpoch).Succeeded);

    [Fact]
    public void UnsafeAnswersOrSafetyModeBlockJointFlows()
    {
        var safety = PersonalSafety.For(Guid.NewGuid());
        Assert.False(safety.BlocksJointFlows);

        safety.RecordCheck(Answers(afraid: "sometimes"), DateTimeOffset.UnixEpoch);
        Assert.True(safety.BlocksJointFlows);

        safety.ClearCheck(DateTimeOffset.UnixEpoch);
        Assert.False(safety.BlocksJointFlows);

        safety.SetSafetyMode(true, DateTimeOffset.UnixEpoch);
        Assert.True(safety.BlocksJointFlows);
    }

    /// <summary>Changing the questions must be a deliberate new version that goes back to clinical review.</summary>
    [Fact]
    public void ContentIsPinnedForClinicalReview()
    {
        Assert.Equal(1, SafetyContent.Version);
        Assert.Equal(PinnedHash, SafetyContent.ContentHash);
    }

    [Fact]
    public void CopyNeverClaimsTheAppWillActInAnEmergency()
    {
        var claims = new Regex(@"\b(we|we'll|we will|bridgetogether will|the app will|this app will)\b[^.]{0,40}\b(call|contact|alert|notify|send help|dispatch|monitor|report)", RegexOptions.IgnoreCase);
        Assert.All(SafetyCopy.All, text => Assert.False(claims.IsMatch(text), text));
        Assert.Contains("can't contact anyone", SafetyCopy.ImmediateDanger, StringComparison.Ordinal);
        Assert.Contains("doesn't monitor", SafetyCopy.ImmediateDanger, StringComparison.Ordinal);
    }

    [Fact]
    public void CopyDoesNotPushReconciliationOrDiagnose()
    {
        var pressure = new Regex(@"\b(should (stay|reconcile|forgive|work it out)|must (talk|reconcile)|save (your|the) relationship|diagnos|abuser|narcissis)", RegexOptions.IgnoreCase);
        Assert.All(SafetyCopy.All.Concat(SafetyContent.Questions.Select(q => q.Text)), text => Assert.False(pressure.IsMatch(text), text));
    }

    private const string PinnedHash = "d0b63906c6550df1231fbd0a077812d494b950792d2fe43b6f9157d645b706d7";
}
