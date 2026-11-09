using System.Net;
using Couples.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Couples.Web.Email;

/// <summary>
/// Development-only sender that writes emails to a local, git-ignored folder so account
/// confirmation and password reset can be exercised without a mail provider. Refuses to run
/// outside the Development environment and the local staging rehearsal (deploy/staging), which
/// otherwise runs with production settings.
/// </summary>
internal sealed partial class PickupDirectoryEmailSender : IEmailSender<ApplicationUser>
{
    /// <summary>The environment name used by the local staging rehearsal.</summary>
    public const string RehearsalEnvironment = "Rehearsal";

    private readonly string directory;
    private readonly ILogger<PickupDirectoryEmailSender> logger;

    public PickupDirectoryEmailSender(IOptions<EmailOptions> options, IHostEnvironment environment, ILogger<PickupDirectoryEmailSender> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);
        if (!environment.IsDevelopment() && !environment.IsEnvironment(RehearsalEnvironment))
        {
            throw new InvalidOperationException("The pickup-directory email sender is for local development only.");
        }

        directory = Path.GetFullPath(options.Value.PickupDirectory, environment.ContentRootPath);
        this.logger = logger;
    }

    // Identity pages pass links already HTML-encoded (HtmlEncoder.Default.Encode), so they are
    // inserted as-is; encoding again would break the query string.
    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) =>
        WriteAsync(user, email, "Confirm your email", $"<p>Confirm your account: <a href=\"{confirmationLink}\">confirm email</a></p>");

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
        WriteAsync(user, email, "Reset your password", $"<p>Reset your password: <a href=\"{resetLink}\">reset password</a></p>");

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
        WriteAsync(user, email, "Reset your password", $"<p>Your password reset code: {WebUtility.HtmlEncode(resetCode)}</p>");

    private async Task WriteAsync(ApplicationUser user, string email, string subject, string body)
    {
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{user.Id:N}.html");
        var html = $"<html><body><p>To: {WebUtility.HtmlEncode(email)}</p><h1>{WebUtility.HtmlEncode(subject)}</h1>{body}</body></html>";
        await File.WriteAllTextAsync(file, html);
        LogWritten(logger, user.Id, directory);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Development email for user {UserId} written to {Directory}")]
    private static partial void LogWritten(ILogger logger, Guid userId, string directory);
}
