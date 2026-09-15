using Ged.Core.Ports;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ged.Api.Diagnostics;

/// <summary>Reports whether the database answers.</summary>
/// <param name="connections">Opens a read connection.</param>
/// <remarks>
/// Opens a connection and runs the cheapest possible statement. A health check that queries real
/// data measures the data as much as the dependency, and turns a slow table into a failed
/// deployment.
/// </remarks>
internal sealed class DatabaseHealthCheck(IDbConnectionFactory connections) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await connections.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();

            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken);

            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("The database did not answer.", ex);
        }
    }
}
