using Couples.Application.Observability;
using Couples.Infrastructure.Observability;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Couples.Web.Observability;

/// <summary>
/// Error monitoring and anonymous metrics over OpenTelemetry (ADR-0014). Off unless
/// <c>Observability:OtlpEndpoint</c> is set. What leaves the app is scrubbed: traces carry the
/// route template, never the URL, query, client address or user agent; logs are warnings and
/// errors only, with exception messages removed and emails and invite tokens masked.
/// </summary>
public static class Telemetry
{
    public const string EndpointKey = "Observability:OtlpEndpoint";

    public static WebApplicationBuilder AddTelemetry(this WebApplicationBuilder builder, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var endpoint = builder.Configuration[EndpointKey];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return builder;
        }

        var otlp = new Uri(endpoint);
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName, serviceVersion: typeof(Telemetry).Assembly.GetName().Version?.ToString()))
            .WithTracing(t => t
                .AddAspNetCoreInstrumentation(o =>
                {
                    o.RecordException = false;
                    o.Filter = context => !context.Request.Path.StartsWithSegments("/health") && !Path.HasExtension(context.Request.Path.Value);
                })
                .AddHttpClientInstrumentation(o => o.RecordException = false)
                .AddProcessor(new TelemetryScrubbing.ScrubbingActivityProcessor())
                .AddOtlpExporter(o => o.Endpoint = otlp))
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(CouplesMetrics.MeterName)
                .AddOtlpExporter(o => o.Endpoint = otlp));

        builder.Logging.AddOpenTelemetry(o =>
        {
            o.IncludeFormattedMessage = true;
            o.IncludeScopes = false;
            o.ParseStateValues = false;
            o.AddProcessor(new TelemetryScrubbing.ScrubbingLogProcessor());
            o.AddOtlpExporter(e => e.Endpoint = otlp);
        });
        builder.Logging.AddFilter<OpenTelemetryLoggerProvider>(string.Empty, LogLevel.Warning);
        return builder;
    }
}
