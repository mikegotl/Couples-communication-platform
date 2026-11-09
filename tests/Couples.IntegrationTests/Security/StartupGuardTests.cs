using Couples.Infrastructure.Security;
using Couples.Web.Components.Layout;

namespace Couples.IntegrationTests.Security;

/// <summary>Configuration guards that stop an insecure production start (ADR-0013).</summary>
public sealed class StartupGuardTests
{
    [Theory]
    [InlineData("Host=db;Database=c;Username=u;Password=p")]
    [InlineData("Host=db;Database=c;Username=u;Password=p;SSL Mode=Disable")]
    [InlineData("Host=db;Database=c;Username=u;Password=p;SSL Mode=Prefer")]
    public void PlaintextDatabaseConnectionsAreRefused(string connectionString) =>
        Assert.Throws<InvalidOperationException>(() => DatabaseTransportSecurity.Ensure(connectionString, requireTls: true));

    [Theory]
    [InlineData("Host=db;Database=c;Username=u;Password=p;SSL Mode=VerifyFull")]
    [InlineData("Host=db;Database=c;Username=u;Password=p;SSL Mode=Require")]
    public void TlsConnectionsAreAccepted(string connectionString) =>
        DatabaseTransportSecurity.Ensure(connectionString, requireTls: true);

    [Fact]
    public void LocalDevelopmentCanOptOut() =>
        DatabaseTransportSecurity.Ensure("Host=localhost;Database=c", requireTls: false);

    [Fact]
    public void ProductionWithoutAKeyRingCertificateDoesNotStart()
    {
        using var factory = new CouplesWebFactory().WithWebHostBuilder(b => b.UseSetting("DataProtection:AllowUnprotectedKeys", "false"));
        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("DataProtection:CertificatePath", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("workspace", "Your workspace · BridgeTogether")]
    [InlineData("messages/received/0198f3c2-0000-7000-8000-000000000000", "Message from your partner · BridgeTogether")]
    [InlineData("messages/new?replyTo=1", "Write a message · BridgeTogether")]
    [InlineData("messages", "Messages · BridgeTogether")]
    [InlineData("exercises/attempt/x", "Exercise · BridgeTogether")]
    [InlineData("", "BridgeTogether")]
    [InlineData("workspaces-elsewhere", "BridgeTogether")]
    public void EveryInteractiveRouteHasATitle(string path, string expected) =>
        Assert.Equal(expected, RouteTitles.For(path));
}
