using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;   // Npgsql.OpenTelemetry 10 moved AddNpgsql() into the Npgsql namespace
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;
using StackExchange.Redis;

namespace LankaMart.BuildingBlocks;

public sealed class TraceEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (Activity.Current is { } a)
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("trace_id", a.TraceId.ToString()));
        }
    }
}

public static class PlatformExtensions
{
    /// <summary>Call first in Program.cs: logging + tracing + error handling + health + OpenAPI.</summary>
    public static WebApplicationBuilder AddPlatformDefaults(this WebApplicationBuilder builder, string serviceName)
    {
        // ---------- Logging: readable in dev, JSON in containers (Loki/ELK friendly) ----------
        builder.Services.AddSerilog((_, lc) =>
        {
            lc.MinimumLevel.Information()
              .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
              .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
              .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
              .ReadFrom.Configuration(builder.Configuration)      // lets appsettings override levels
              .Enrich.FromLogContext()
              .Enrich.WithProperty("service", serviceName)
              .Enrich.With<TraceEnricher>();

            if (builder.Environment.IsDevelopment())
            {
                lc.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{service}] {Message:lj}{NewLine}{Exception}");
            }
            else
            {
                lc.WriteTo.Console(new RenderedCompactJsonFormatter());
            }
        });

        // ---------- Tracing (OpenTelemetry) → Jaeger, or the Aspire dashboard ----------
        // Otel:Endpoint set   → export there (e.g. Jaeger at http://localhost:4317), as in the build guide.
        // Otel:Endpoint empty → if Aspire (or anything else) set OTEL_EXPORTER_OTLP_ENDPOINT, use the standard OTEL_* env vars
        //                       (endpoint + headers + protocol) so traces/metrics show up in the Aspire dashboard.
        // Neither             → tracing stays in-process.
        var otlp = builder.Configuration["Otel:Endpoint"];
        var useEnvOtlp = string.IsNullOrWhiteSpace(otlp) && !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(t =>
            {
                t.AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health"))
                 .AddHttpClientInstrumentation()
                 .AddNpgsql()
                 .AddSource("MassTransit");
                if (!string.IsNullOrWhiteSpace(otlp))
                {
                    t.AddOtlpExporter(o => o.Endpoint = new Uri(otlp));
                }
                else if (useEnvOtlp)
                {
                    t.AddOtlpExporter();
                }
            })
            .WithMetrics(m =>
            {
                m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation();
                if (useEnvOtlp)
                {
                    m.AddOtlpExporter();
                }
            });

        // ---------- Errors, health, OpenAPI ----------
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.Services.AddHealthChecks();
        builder.Services.AddOpenApi();
        return builder;
    }

    /// <summary>Call after Build(): exception handler, request logging, /health/*, OpenAPI + Scalar UI (dev only).</summary>
    public static WebApplication UsePlatformDefaults(this WebApplication app)
    {
        app.UseExceptionHandler();      // uses GlobalExceptionHandler
        app.UseStatusCodePages();       // empty 401/403/404 → Problem Details JSON
        app.UseSerilogRequestLogging(); // one structured line per request

        // /health/live  = "is the process up?"        → Kubernetes LIVENESS probe (restart me if this fails)
        // /health/ready = "can I serve traffic now?"  → Kubernetes READINESS probe (stop routing to me if this fails)
        // Mixing them up is the classic cause of restart loops (architecture doc §7.4).
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = r => r.Tags.Contains("ready") }).AllowAnonymous();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi().AllowAnonymous();                  // /openapi/v1.json
            app.MapScalarApiReference().AllowAnonymous();       // /scalar/v1  (interactive API docs)
        }
        return app;
    }

    public static IServiceCollection AddPlatformPostgresHealth(this IServiceCollection s, IConfiguration c)
    {
        s.AddHealthChecks().AddNpgSql(c.GetConnectionString("Default")!, name: "postgres", tags: ["ready"]);
        return s;
    }

    public static IServiceCollection AddPlatformRedis(this IServiceCollection s, IConfiguration c)
    {
        var cs = c["Redis:ConnectionString"] ?? "localhost:6379";
        var opts = ConfigurationOptions.Parse(cs);
        opts.AbortOnConnectFail = false;       // don't crash at startup if Redis is still booting; it reconnects
        s.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(opts));
        s.AddHealthChecks().AddRedis(sp => sp.GetRequiredService<IConnectionMultiplexer>(), name: "redis", tags: ["ready"]);
        return s;
    }
}
