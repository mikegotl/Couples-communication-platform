using System.Security;
using Couples.Application.Sms;
using Couples.Infrastructure.Sms;
using Microsoft.Extensions.Options;

namespace Couples.Web.Sms;

/// <summary>
/// Twilio's webhook for incoming texts (ADR-0020). Anonymous by necessity, so every request must
/// carry a valid Twilio signature for the configured public URL; anything else gets 403. Answers
/// 404 while texts are off, so the endpoint doesn't show it exists.
/// </summary>
internal static class SmsEndpoints
{
    public const string TwilioPath = "/sms/twilio";

    public static IEndpointRouteBuilder MapSmsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(TwilioPath, HandleTwilioAsync).DisableAntiforgery().AllowAnonymous();
        return app;
    }

    private static async Task<IResult> HandleTwilioAsync(HttpContext context, IOptions<SmsOptions> options, Couples.Application.Abstractions.IFeatureFlags flags, ISmsInbound inbound)
    {
        // Plain status codes for Twilio, not the HTML "not found" page re-executed as a form post.
        if (context.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodePagesFeature>() is { } pages)
        {
            pages.Enabled = false;
        }

        var sms = options.Value;
        if (sms.Provider != "Twilio" || !sms.IsConfigured || !await flags.IsEnabledAsync(Couples.Application.FeatureFlags.SmsMessaging, context.RequestAborted))
        {
            return Results.NotFound();
        }

        if (!context.Request.HasFormContentType)
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var fields = form.Select(f => new KeyValuePair<string, string>(f.Key, f.Value.ToString())).ToList();
        var url = sms.PublicBaseUrl!.ToString().TrimEnd('/') + TwilioPath;
        if (!TwilioSignature.IsValid(sms.Twilio.AuthToken, url, fields, context.Request.Headers["X-Twilio-Signature"]))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var reply = await inbound.HandleAsync(form["From"].ToString(), form["Body"].ToString(), context.RequestAborted);
        var twiml = reply is null
            ? "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Response/>"
            : $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><Response><Message>{SecurityElement.Escape(reply)}</Message></Response>";
        return Results.Content(twiml, "application/xml");
    }
}

/// <summary>
/// Texts due notifications every 15 seconds. Runs in every web instance; each notification is
/// claimed in the database first, so it is texted once. Does nothing while texts are off.
/// </summary>
internal sealed partial class SmsDispatchService(IServiceScopeFactory scopes, ILogger<SmsDispatchService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ISmsDispatcher>().DispatchDueAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex.GetType().Name);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Text dispatch run failed: {ErrorType}")]
    private static partial void LogFailed(ILogger logger, string errorType);
}
