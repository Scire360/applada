using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Applada.ServiceDefaults;

public static class Extensions
{
    private const string ReadyTag = "ready";

    /// <summary>
    /// Configura OpenTelemetry (traces, métricas e logs) e os health checks padrão.
    /// O exportador OTLP só é ligado quando OTEL_EXPORTER_OTLP_ENDPOINT está definido.
    /// </summary>
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureOpenTelemetry();
        builder.Services.AddHealthChecks();

        return builder;
    }

    /// <summary>
    /// Mapeia /health/live (sem checks) e /health/ready (checks com a tag "ready"), ambos anônimos.
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var health = app.MapGroup("/health").AllowAnonymous();

        health.MapHealthChecks("/live", new HealthCheckOptions { Predicate = _ => false });
        health.MapHealthChecks("/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadyTag) });

        return app;
    }

    private static void ConfigureOpenTelemetry(this IHostApplicationBuilder builder)
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        var otel = builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation())
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddSource("Npgsql"));

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            otel.UseOtlpExporter();
        }
    }
}
