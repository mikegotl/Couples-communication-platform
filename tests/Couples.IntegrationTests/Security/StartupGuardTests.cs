using Couples.Infrastructure.Security;
using Couples.Web.Components.Layout;
using Couples.Web.Security;

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
    [InlineData(false, "secret-pw")]
    [InlineData(true, null)]
    public void KeyRingCertificateLoadsFromABinaryOrBase64File(bool base64, string? password)
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=keyring-test", rsa, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        var pfx = created.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pkcs12, password);
        var path = Path.GetTempFileName();
        try
        {
            // Key Vault hands out a certificate's secret as base64 text with no password.
            if (base64)
            {
                File.WriteAllText(path, Convert.ToBase64String(pfx) + "\n");
            }
            else
            {
                File.WriteAllBytes(path, pfx);
            }

            using var loaded = KeyRingCertificate.Load(path, password);
            Assert.Equal(created.Thumbprint, loaded.Thumbprint);
            Assert.True(loaded.HasPrivateKey);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void APrivatePilotWithoutAnInvitationCodeDoesNotStart()
    {
        using var factory = new CouplesWebFactory().WithWebHostBuilder(b => b.UseSetting("Pilot:AccessCodeSha256", ""));
        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("Pilot:AccessCodeSha256", error.ToString(), StringComparison.Ordinal);
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
