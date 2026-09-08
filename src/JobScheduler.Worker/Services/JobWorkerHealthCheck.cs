using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace JobScheduler.Worker;

internal sealed class JobWorkerHealthCheck(JobWorkerState state) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object> { ["active_jobs"] = state.ActiveJobs };
        var result = state.IsRunning && !state.IsDraining
            ? HealthCheckResult.Healthy("Worker is accepting jobs.", data)
            : HealthCheckResult.Unhealthy("Worker is not ready to accept jobs.", data: data);
        return Task.FromResult(result);
    }
}
