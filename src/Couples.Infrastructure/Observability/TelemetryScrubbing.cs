using System.Diagnostics;
using System.Text.RegularExpressions;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace Couples.Infrastructure.Observability;

/// <summary>
/// Scrubs telemetry before export (ADR-0014): spans keep the route template but not the URL,
/// query, client address or user agent; logs lose exception messages and structured values, and
/// emails and invitation tokens in log text are masked.
/// </summary>
public static partial class TelemetryScrubbing
{
    private static readonly string[] DroppedTags =
    [
        "url.full", "url.path", "url.query", "url.original", "http.url", "http.target",
        "client.address", "client.port", "network.peer.address", "network.peer.port", "user_agent.original",
        "http.request.header.cookie", "http.request.header.authorization", "enduser.id",
    ];

    /// <summary>Masks email addresses and invitation tokens in free text.</summary>
    public static string Mask(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return InvitePath().Replace(EmailPattern().Replace(text, "[email]"), "/invite/[token]");
    }

    [GeneratedRegex(@"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"/invite/[^\s/?#""']+", RegexOptions.IgnoreCase)]
    private static partial Regex InvitePath();

    /// <summary>Removes URLs, addresses and agents from spans and names them by route template.</summary>
    public sealed class ScrubbingActivityProcessor : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity data)
        {
            ArgumentNullException.ThrowIfNull(data);
            foreach (var tag in DroppedTags)
            {
                data.SetTag(tag, null);
            }

            var method = data.GetTagItem("http.request.method") as string;
            var route = data.GetTagItem("http.route") as string;
            if (method is not null)
            {
                data.DisplayName = route is null ? method : $"{method} {route}";
            }
            else
            {
                data.DisplayName = Mask(data.DisplayName);
            }
        }
    }

    /// <summary>Keeps the exception type and stack, drops its message, and masks the log text.</summary>
    public sealed class ScrubbingLogProcessor : BaseProcessor<LogRecord>
    {
        public override void OnEnd(LogRecord data)
        {
            ArgumentNullException.ThrowIfNull(data);
            if (data.FormattedMessage is { } message)
            {
                data.FormattedMessage = Mask(message);
            }

            if (data.Body is { } body)
            {
                data.Body = Mask(body);
            }

            if (data.Exception is { } exception and not RedactedException)
            {
                data.Exception = new RedactedException(exception);
            }

            data.Attributes = null;
        }
    }

    /// <summary>Stands in for an exception whose message might contain user data.</summary>
    public sealed class RedactedException : Exception
    {
        private readonly string? stackTrace;

        public RedactedException()
        {
        }

        public RedactedException(string message)
            : base(message)
        {
        }

        public RedactedException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        public RedactedException(Exception original)
            : base((original ?? throw new ArgumentNullException(nameof(original))).GetType().FullName + " (message removed)")
        {
            OriginalType = original.GetType().FullName;
            stackTrace = original.StackTrace;
        }

        public string? OriginalType { get; }

        public override string? StackTrace => stackTrace;
    }
}
