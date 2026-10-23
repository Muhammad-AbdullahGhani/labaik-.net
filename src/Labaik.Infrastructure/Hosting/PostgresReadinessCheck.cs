using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Labaik.Infrastructure.Hosting;

internal sealed class PostgresReadinessCheck(NpgsqlDataSource dataSource, string schema) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT has_schema_privilege(current_user, $1, 'USAGE')";
            command.Parameters.AddWithValue(schema);
            return await command.ExecuteScalarAsync(cancellationToken) is true
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Database unavailable.");
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException or OperationCanceledException)
        {
            // Do not attach the provider exception: it can disclose database names or connection details.
            return HealthCheckResult.Unhealthy("Database unavailable.");
        }
    }
}
