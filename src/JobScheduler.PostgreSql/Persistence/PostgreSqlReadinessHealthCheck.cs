using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace JobScheduler.PostgreSql;

internal sealed class PostgreSqlReadinessHealthCheck(PostgreSqlMigrator migrator) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try { await migrator.ValidateAsync(cancellationToken); return HealthCheckResult.Healthy("Storage and schema are ready."); }
        catch (Exception exception) { return HealthCheckResult.Unhealthy("Storage or schema is not ready.", exception); }
    }
}
