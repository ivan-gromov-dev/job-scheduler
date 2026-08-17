using JobScheduler.Core.Handlers;
using JobScheduler.Core.Jobs;
using Microsoft.Extensions.Options;

namespace JobScheduler.Worker;

public sealed class JobWorker(
    IJobStore store,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<JobWorkerOptions> options,
    ILogger<JobWorker> logger) : BackgroundService
{
    private static readonly Action<ILogger, Guid, Exception?> LogJobFailed =
        LoggerMessage.Define<Guid>(
            LogLevel.Error,
            new EventId(1, nameof(LogJobFailed)),
            "Job {JobId} failed");

    private readonly JobWorkerOptions settings = Validate(options.Value);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workers = Enumerable.Range(0, settings.MaxConcurrency)
            .Select(_ => ProcessLoopAsync(stoppingToken));
        return Task.WhenAll(workers);
    }

    private async Task ProcessLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var lease = await store.ClaimAsync(settings.LeaseDuration, stoppingToken);
            if (lease is null)
            {
                await Task.Delay(settings.PollInterval, timeProvider, stoppingToken);
                continue;
            }

            await ProcessAsync(lease, stoppingToken);
        }
    }

    private async Task ProcessAsync(JobLease lease, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IJobDispatcher>();
            await dispatcher.DispatchAsync(lease.Job, stoppingToken);
            await store.CompleteAsync(lease, CancellationToken.None);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Leave the lease in place. Another worker may reclaim it after expiration.
        }
        catch (Exception exception)
        {
            LogJobFailed(logger, lease.Job.Id, exception);
            await store.FailAsync(lease, exception.Message, CancellationToken.None);
        }
    }

    private static JobWorkerOptions Validate(JobWorkerOptions value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value.MaxConcurrency, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value.PollInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value.LeaseDuration, TimeSpan.Zero);
        return value;
    }
}
