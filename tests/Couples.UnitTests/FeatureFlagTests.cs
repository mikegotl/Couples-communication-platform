using Couples.Application;

namespace Couples.UnitTests;

public sealed class FeatureFlagTests
{
    [Fact]
    public void Phase2FlagNameIsStable()
    {
        // Configuration and deployment tooling refer to this exact name.
        Assert.Equal("Phase2JointChat", FeatureFlags.Phase2JointChat);
    }
}
