using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace JobScheduler.Worker;

internal sealed class ScheduleMaterializerHealthCheck(ScheduleMaterializerState state) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object> { ["last_successful_run"] = state.LastSuccessfulRun?.ToString("O") ?? "never" };
        return Task.FromResult(state.IsRunning && state.FatalError is null
            ? HealthCheckResult.Healthy("Schedule materializer is running.", data)
            : HealthCheckResult.Unhealthy("Schedule materializer is not healthy.", state.FatalError, data));
    }
}
