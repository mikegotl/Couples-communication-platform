using Couples.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace Couples.Web.Email;

/// <summary>
/// Used until an email provider is chosen. Sends nothing and never logs addresses, links or codes.
/// </summary>
internal sealed partial class UnconfiguredEmailSender(ILogger<UnconfiguredEmailSender> logger) : IEmailSender<ApplicationUser>
{
    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) => Skip(user);

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) => Skip(user);

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) => Skip(user);

    private Task Skip(ApplicationUser user)
    {
        LogNotSent(logger, user.Id);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email delivery is not configured; account email for user {UserId} was not sent")]
    private static partial void LogNotSent(ILogger logger, Guid userId);
}
