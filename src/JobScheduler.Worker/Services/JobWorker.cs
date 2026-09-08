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
            await store.PurgeDeadLettersAsync(timeProvider.GetUtcNow().Subtract(settings.DeadLetterRetention), stoppingToken);
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
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        using var timeoutCancellation = new CancellationTokenSource();
        var timeoutTask = Task.Delay(settings.ExecutionTimeout, timeProvider, timeoutCancellation.Token);
        var renewalTask = RenewLeaseAsync(lease, execution.Token);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IJobDispatcher>();
            var dispatchTask = dispatcher.DispatchAsync(lease.Job, execution.Token);
            if (await Task.WhenAny(dispatchTask, timeoutTask) == timeoutTask)
            {
                execution.Cancel();
                await HandleFailureAsync(lease, new JobFailure(JobFailureKind.Timeout, $"Execution exceeded {settings.ExecutionTimeout}."));
                return;
            }

            await dispatchTask;
            await store.CompleteAsync(lease, CancellationToken.None);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Leave the lease in place. Another worker may reclaim it after expiration.
        }
        catch (Exception exception)
        {
            LogJobFailed(logger, lease.Job.Id, exception);
            var kind = exception is JobExecutionException classified ? classified.Kind :
                exception is OperationCanceledException ? JobFailureKind.Cancellation : JobFailureKind.Transient;
            await HandleFailureAsync(lease, new JobFailure(kind, exception.Message));
        }
        finally
        {
            timeoutCancellation.Cancel();
            execution.Cancel();
            try { await renewalTask; } catch (OperationCanceledException) { }
        }
    }

    private async Task HandleFailureAsync(JobLease lease, JobFailure failure)
    {
        var retryable = failure.Kind is JobFailureKind.Transient or JobFailureKind.Timeout;
        if (retryable && lease.Job.Attempt < settings.Retry.MaxAttempts)
        {
            var retryAt = timeProvider.GetUtcNow().Add(settings.Retry.GetDelay(lease.Job.Id, lease.Job.Attempt));
            await store.RetryAsync(lease, failure, retryAt, CancellationToken.None);
        }
        else
        {
            await store.DeadLetterAsync(lease, failure, CancellationToken.None);
        }
    }

    private async Task RenewLeaseAsync(JobLease lease, CancellationToken cancellationToken)
    {
        var current = lease;
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(settings.LeaseRenewalInterval, timeProvider, cancellationToken);
            current = await store.RenewLeaseAsync(current, settings.LeaseDuration, cancellationToken);
            if (current is null) return;
        }
    }

    private static JobWorkerOptions Validate(JobWorkerOptions value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value.MaxConcurrency, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value.PollInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value.LeaseDuration, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value.LeaseRenewalInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(value.LeaseRenewalInterval, value.LeaseDuration);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value.ExecutionTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value.DeadLetterRetention, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(value.Retry.MaxAttempts, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(value.Retry.InitialDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(value.Retry.MaxDelay, value.Retry.InitialDelay);
        ArgumentOutOfRangeException.ThrowIfLessThan(value.Retry.BackoffMultiplier, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(value.Retry.JitterFactor, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Retry.JitterFactor, 1);
        return value;
    }
}
