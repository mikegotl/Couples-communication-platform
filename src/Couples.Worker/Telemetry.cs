using Couples.Application.Observability;
using Couples.Infrastructure.Observability;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace Couples.Worker;

/// <summary>Metrics and scrubbed warning/error logs for the worker; off unless an endpoint is set.</summary>
internal static class Telemetry
{
    public static void AddTelemetry(this HostApplicationBuilder builder)
    {
        var endpoint = builder.Configuration["Observability:OtlpEndpoint"];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return;
        }

        var otlp = new Uri(endpoint);
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService("bridgetogether-worker"))
            .WithMetrics(m => m.AddRuntimeInstrumentation().AddMeter(CouplesMetrics.MeterName).AddOtlpExporter(o => o.Endpoint = otlp));
        builder.Logging.AddOpenTelemetry(o =>
        {
            o.IncludeFormattedMessage = true;
            o.AddProcessor(new TelemetryScrubbing.ScrubbingLogProcessor());
            o.AddOtlpExporter(e => e.Endpoint = otlp);
        });
        builder.Logging.AddFilter<OpenTelemetryLoggerProvider>(string.Empty, LogLevel.Warning);
    }
}
