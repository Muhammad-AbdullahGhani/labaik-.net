using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Labaik.Infrastructure.Hosting;

public static class ServiceHost
{
    public static void AddHealthOnlyHost(WebApplicationBuilder builder, string schema)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.UseUtcTimestamp = true;
            options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
        });
        // Framework request logs can include arbitrary URLs/query strings; log only our fixed fields below.
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.None);
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);
        builder.Logging.AddFilter("Npgsql", LogLevel.None);
        builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.None);
        builder.Services.AddSingleton(services =>
        {
            var connectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("Database");
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("ConnectionStrings__Database must be configured.");
            return NpgsqlDataSource.Create(connectionString);
        });
        builder.Services.AddHealthChecks().AddCheck<PostgresReadinessCheck>("postgres", tags: ["ready"],
            timeout: TimeSpan.FromSeconds(3));
        builder.Services.AddSingleton(services => new PostgresReadinessCheck(
            services.GetRequiredService<NpgsqlDataSource>(), schema));
    }

    public static void MapHealthOnlyHost(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            // Generate locally rather than accepting an arbitrary header that could contain personal data.
            var correlationId = Guid.NewGuid().ToString("N");
            context.TraceIdentifier = correlationId;
            context.Response.Headers["X-Correlation-ID"] = correlationId;
            using var scope = app.Logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId });
            var started = Stopwatch.GetTimestamp();
            try
            {
                await next(context);
            }
            finally
            {
                app.Logger.LogInformation("Request completed with status {StatusCode} in {ElapsedMs} ms",
                    context.Response.StatusCode, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
        });
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
    }
}
