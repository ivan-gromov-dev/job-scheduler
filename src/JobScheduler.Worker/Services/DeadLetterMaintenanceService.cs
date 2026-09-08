using JobScheduler.Core.Jobs;
using Microsoft.Extensions.Options;

namespace JobScheduler.Worker;

internal sealed class DeadLetterMaintenanceService(
    IJobStore store,
    TimeProvider timeProvider,
    IOptions<JobWorkerOptions> options,
    JobWorkerState state) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        using var timer = new PeriodicTimer(settings.MaintenanceInterval, timeProvider);
        do
        {
            if (state.IsDraining) return;
            DeadLetterMaintenanceResult result;
            do
            {
                result = await store.PurgeDeadLettersBatchAsync(
                    timeProvider.GetUtcNow().Subtract(settings.DeadLetterRetention),
                    settings.MaintenanceBatchSize,
                    stoppingToken);
            }
            while (result.IsOwner && result.PurgedCount == settings.MaintenanceBatchSize && !state.IsDraining);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
