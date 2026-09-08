using JobScheduler.Core.Handlers;
using JobScheduler.Core.Jobs;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace JobScheduler.Worker;

public sealed class JobWorker(
    IJobStore store,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<JobWorkerOptions> options,
    ILogger<JobWorker> logger,
    JobWorkerState state) : BackgroundService
{
    private static readonly Action<ILogger, Guid, Exception?> LogJobFailed =
        LoggerMessage.Define<Guid>(
            LogLevel.Error,
            new EventId(1, nameof(LogJobFailed)),
            "Job {JobId} failed");

    private static readonly Action<ILogger, Guid, int, string, string?, Exception?> LogJobStarted =
        LoggerMessage.Define<Guid, int, string, string?>(LogLevel.Information, new EventId(2, nameof(LogJobStarted)),
            "Executing job {JobId}, attempt {Attempt}, queue {Queue}, correlation {CorrelationId}");

    private static readonly Action<ILogger, Guid, double, Exception?> LogJobCompleted =
        LoggerMessage.Define<Guid, double>(LogLevel.Information, new EventId(3, nameof(LogJobCompleted)),
            "Completed job {JobId} in {ElapsedMilliseconds} ms");

    private readonly JobWorkerOptions settings = Validate(options.Value);

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        state.BeginDrain();
        return base.StopAsync(cancellationToken);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        state.Start();
        var workers = settings.QueueConcurrency.Count == 0
            ? Enumerable.Range(0, settings.MaxConcurrency).Select(_ => ProcessLoopAsync(null, stoppingToken))
            : settings.QueueConcurrency.SelectMany(pair => Enumerable.Range(0, pair.Value)
                .Select(_ => ProcessLoopAsync(pair.Key, stoppingToken)));
        return FinishAsync(Task.WhenAll(workers));
    }

    private async Task FinishAsync(Task workers)
    {
        try { await workers; }
        finally { state.BeginDrain(); state.Stop(); }
    }

    private async Task ProcessLoopAsync(string? queue, CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await store.PurgeDeadLettersAsync(timeProvider.GetUtcNow().Subtract(settings.DeadLetterRetention), stoppingToken);
                var lease = queue is null
                    ? await store.ClaimAsync(settings.LeaseDuration, stoppingToken)
                    : await store.ClaimAsync(settings.LeaseDuration, [queue], stoppingToken);
                if (lease is null)
                {
                    await Task.Delay(settings.PollInterval, timeProvider, stoppingToken);
                    continue;
                }

                await ProcessAsync(lease);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { state.BeginDrain(); }
    }

    private async Task ProcessAsync(JobLease lease)
    {
        state.JobStarted();
        var tags = new TagList { { "job.queue", lease.Job.Queue }, { "job.type", lease.Job.Type } };
        JobSchedulerTelemetry.Claimed.Add(1, tags);
        JobSchedulerTelemetry.Lag.Record(Math.Max(0, (timeProvider.GetUtcNow() - lease.Job.ScheduledAt).TotalSeconds), tags);
        using var activity = JobSchedulerTelemetry.Activities.StartActivity("job.execute", ActivityKind.Consumer);
        activity?.SetTag("job.id", lease.Job.Id).SetTag("job.attempt", lease.Job.Attempt)
            .SetTag("job.queue", lease.Job.Queue).SetTag("job.correlation_id", lease.Job.CorrelationId);
        using var logScope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["JobId"] = lease.Job.Id,
            ["Attempt"] = lease.Job.Attempt,
            ["Queue"] = lease.Job.Queue,
            ["CorrelationId"] = lease.Job.CorrelationId,
        });
        LogJobStarted(logger, lease.Job.Id, lease.Job.Attempt, lease.Job.Queue, lease.Job.CorrelationId, null);
        var started = timeProvider.GetTimestamp();
        using var execution = new CancellationTokenSource();
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
            JobSchedulerTelemetry.Completed.Add(1, tags);
            LogJobCompleted(logger, lease.Job.Id, timeProvider.GetElapsedTime(started).TotalMilliseconds, null);
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            LogJobFailed(logger, lease.Job.Id, exception);
            var kind = exception is JobExecutionException classified ? classified.Kind :
                exception is OperationCanceledException ? JobFailureKind.Cancellation : JobFailureKind.Transient;
            await HandleFailureAsync(lease, new JobFailure(kind, exception.Message));
        }
        finally
        {
            JobSchedulerTelemetry.Duration.Record(timeProvider.GetElapsedTime(started).TotalSeconds, tags);
            timeoutCancellation.Cancel();
            execution.Cancel();
            try { await renewalTask; } catch (OperationCanceledException) { }
            state.JobStopped();
        }
    }

    private async Task HandleFailureAsync(JobLease lease, JobFailure failure)
    {
        var retryable = failure.Kind is JobFailureKind.Transient or JobFailureKind.Timeout;
        if (retryable && lease.Job.Attempt < settings.Retry.MaxAttempts)
        {
            var retryAt = timeProvider.GetUtcNow().Add(settings.Retry.GetDelay(lease.Job.Id, lease.Job.Attempt));
            await store.RetryAsync(lease, failure, retryAt, CancellationToken.None);
            JobSchedulerTelemetry.Retried.Add(1, new TagList { { "job.queue", lease.Job.Queue }, { "failure.kind", failure.Kind.ToString() } });
        }
        else
        {
            await store.DeadLetterAsync(lease, failure, CancellationToken.None);
            JobSchedulerTelemetry.DeadLettered.Add(1, new TagList { { "job.queue", lease.Job.Queue }, { "failure.kind", failure.Kind.ToString() } });
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
        foreach (var queue in value.QueueConcurrency)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(queue.Key);
            ArgumentOutOfRangeException.ThrowIfLessThan(queue.Value, 1);
        }
        ArgumentOutOfRangeException.ThrowIfLessThan(value.Retry.InitialDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(value.Retry.MaxDelay, value.Retry.InitialDelay);
        ArgumentOutOfRangeException.ThrowIfLessThan(value.Retry.BackoffMultiplier, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(value.Retry.JitterFactor, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Retry.JitterFactor, 1);
        return value;
    }
}
