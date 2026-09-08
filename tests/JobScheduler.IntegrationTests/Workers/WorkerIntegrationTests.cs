using JobScheduler.Core.Handlers;
using JobScheduler.Core.Jobs;
using JobScheduler.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace JobScheduler.IntegrationTests;

public sealed class WorkerIntegrationTests
{
    [Fact]
    public async Task HostProcessesImmediateAndDelayedTypedJobs()
    {
        var state = new ProcessingState();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(state);
        services.AddJobHandler<TestJob, TestJobHandler>();
        services.AddJobWorker(options =>
        {
            options.MaxConcurrency = 2;
            options.PollInterval = TimeSpan.FromMilliseconds(10);
        });
        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetRequiredService<IEnumerable<IHostedService>>().OfType<JobWorker>().Single();
        var client = provider.GetRequiredService<IJobClient>();
        var store = provider.GetRequiredService<IJobStore>();
        var immediate = await client.EnqueueAsync(new TestJob("first"));
        var delayed = await client.ScheduleAsync(
            new TestJob("second"),
            DateTimeOffset.UtcNow.AddMilliseconds(100));

        await hostedService.StartAsync(CancellationToken.None);
        await state.Handled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await hostedService.StopAsync(CancellationToken.None);

        Assert.Equal(JobStatus.Succeeded, (await store.GetAsync(immediate.Id))!.Status);
        Assert.Equal(JobStatus.Succeeded, (await store.GetAsync(delayed.Id))!.Status);
    }

    [Fact]
    public async Task WorkerRetriesTransientFailureUntilSuccess()
    {
        var state = new RetryState();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(state);
        services.AddJobHandler<RetryJob, RetryHandler>();
        services.AddJobWorker(options =>
        {
            options.MaxConcurrency = 1;
            options.PollInterval = TimeSpan.FromMilliseconds(5);
            options.LeaseDuration = TimeSpan.FromSeconds(1);
            options.LeaseRenewalInterval = TimeSpan.FromMilliseconds(100);
            options.Retry.InitialDelay = TimeSpan.FromMilliseconds(5);
            options.Retry.MaxDelay = TimeSpan.FromMilliseconds(5);
            options.Retry.JitterFactor = 0;
        });
        await using var provider = services.BuildServiceProvider();
        var worker = provider.GetRequiredService<IEnumerable<IHostedService>>().OfType<JobWorker>().Single();
        var store = provider.GetRequiredService<IJobStore>();
        var job = await provider.GetRequiredService<IJobClient>().EnqueueAsync(new RetryJob());

        await worker.StartAsync(CancellationToken.None);
        await state.Succeeded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        var completed = await store.GetAsync(job.Id);
        Assert.Equal(JobStatus.Succeeded, completed!.Status);
        Assert.Equal(2, completed.Attempt);
    }

    [Fact]
    public async Task WorkerDeadLettersPermanentFailureWithoutRetry()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddJobHandler<PermanentJob, PermanentHandler>();
        services.AddJobWorker(options =>
        {
            options.MaxConcurrency = 1;
            options.PollInterval = TimeSpan.FromMilliseconds(5);
        });
        await using var provider = services.BuildServiceProvider();
        var worker = provider.GetRequiredService<IEnumerable<IHostedService>>().OfType<JobWorker>().Single();
        var store = provider.GetRequiredService<IJobStore>();
        var job = await provider.GetRequiredService<IJobClient>().EnqueueAsync(new PermanentJob());

        await worker.StartAsync(CancellationToken.None);
        await WaitForStatusAsync(store, job.Id, JobStatus.DeadLettered);
        await worker.StopAsync(CancellationToken.None);

        var failed = await store.GetAsync(job.Id);
        Assert.Equal(1, failed!.Attempt);
        Assert.Equal(JobFailureKind.Permanent, failed.FailureKind);
    }

    [Fact]
    public async Task WorkerCancelsTimedOutHandlerAndDeadLettersAtAttemptLimit()
    {
        var state = new TimeoutState();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(state);
        services.AddJobHandler<TimeoutJob, TimeoutHandler>();
        services.AddJobWorker(options =>
        {
            options.MaxConcurrency = 1;
            options.PollInterval = TimeSpan.FromMilliseconds(5);
            options.ExecutionTimeout = TimeSpan.FromMilliseconds(30);
            options.Retry.MaxAttempts = 1;
        });
        await using var provider = services.BuildServiceProvider();
        var worker = provider.GetRequiredService<IEnumerable<IHostedService>>().OfType<JobWorker>().Single();
        var store = provider.GetRequiredService<IJobStore>();
        var job = await provider.GetRequiredService<IJobClient>().EnqueueAsync(new TimeoutJob());

        await worker.StartAsync(CancellationToken.None);
        await WaitForStatusAsync(store, job.Id, JobStatus.DeadLettered);
        await state.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(JobFailureKind.Timeout, (await store.GetAsync(job.Id))!.FailureKind);
    }

    [Fact]
    public async Task TimeoutWaitsForNonCooperativeInvocationBeforeRetrying()
    {
        var state = new NonCooperativeTimeoutState();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(state);
        services.AddJobHandler<NonCooperativeTimeoutJob, NonCooperativeTimeoutHandler>();
        services.AddJobWorker(options =>
        {
            options.MaxConcurrency = 1;
            options.PollInterval = TimeSpan.FromMilliseconds(5);
            options.ExecutionTimeout = TimeSpan.FromMilliseconds(30);
            options.LeaseDuration = TimeSpan.FromSeconds(2);
            options.LeaseRenewalInterval = TimeSpan.FromMilliseconds(20);
            options.Retry.InitialDelay = TimeSpan.Zero;
            options.Retry.MaxDelay = TimeSpan.Zero;
            options.Retry.JitterFactor = 0;
        });
        await using var provider = services.BuildServiceProvider();
        var worker = provider.GetRequiredService<IEnumerable<IHostedService>>().OfType<JobWorker>().Single();
        var store = provider.GetRequiredService<IJobStore>();
        var job = await provider.GetRequiredService<IJobClient>().EnqueueAsync(new NonCooperativeTimeoutJob());

        await worker.StartAsync(CancellationToken.None);
        await state.TimedOut.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(80);
        Assert.Equal(1, state.Starts);
        Assert.Equal(JobStatus.Processing, (await store.GetAsync(job.Id))!.Status);

        state.Release.TrySetResult();
        await state.Succeeded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForStatusAsync(store, job.Id, JobStatus.Succeeded);
        await worker.StopAsync(CancellationToken.None);
        Assert.Equal(2, state.Starts);
    }

    [Fact]
    public async Task LostLeaseCancelsHandlerAndLeavesLifecycleToNewOwner()
    {
        var state = new LostLeaseState();
        var store = new LeaseRejectingStore(new InMemoryJobStore(TimeProvider.System));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IJobStore>(store);
        services.AddSingleton(state);
        services.AddJobHandler<LostLeaseJob, LostLeaseHandler>();
        services.AddJobWorker(options =>
        {
            options.MaxConcurrency = 1;
            options.PollInterval = TimeSpan.FromMilliseconds(5);
            options.LeaseDuration = TimeSpan.FromSeconds(1);
            options.LeaseRenewalInterval = TimeSpan.FromMilliseconds(20);
        });
        await using var provider = services.BuildServiceProvider();
        var worker = provider.GetRequiredService<IEnumerable<IHostedService>>().OfType<JobWorker>().Single();
        var job = await provider.GetRequiredService<IJobClient>().EnqueueAsync(new LostLeaseJob());

        await worker.StartAsync(CancellationToken.None);
        await state.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(JobStatus.Processing, (await store.GetAsync(job.Id))!.Status);
        Assert.Equal(0, store.LifecycleWrites);
    }

    [Fact]
    public async Task ReadinessTurnsUnhealthyWhileWorkerGracefullyDrainsActiveJob()
    {
        var state = new DrainState();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(state);
        services.AddJobHandler<DrainJob, DrainHandler>();
        services.AddJobWorker(options =>
        {
            options.MaxConcurrency = 1;
            options.PollInterval = TimeSpan.FromMilliseconds(5);
        });
        await using var provider = services.BuildServiceProvider();
        var worker = provider.GetRequiredService<IEnumerable<IHostedService>>().OfType<JobWorker>().Single();
        var health = provider.GetRequiredService<HealthCheckService>();
        await provider.GetRequiredService<IJobClient>().EnqueueAsync(new DrainJob());

        await worker.StartAsync(CancellationToken.None);
        await state.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(HealthStatus.Healthy, (await health.CheckHealthAsync()).Status);

        var stop = worker.StopAsync(CancellationToken.None);
        await Task.Delay(20);
        Assert.False(stop.IsCompleted);
        Assert.Equal(HealthStatus.Unhealthy, (await health.CheckHealthAsync()).Status);
        state.Release.TrySetResult();
        await stop.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task DrainTimeoutCancelsActiveHandler()
    {
        var state = new LostLeaseState();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(state);
        services.AddJobHandler<LostLeaseJob, LostLeaseHandler>();
        services.AddJobWorker(options =>
        {
            options.MaxConcurrency = 1;
            options.PollInterval = TimeSpan.FromMilliseconds(5);
            options.DrainTimeout = TimeSpan.FromMilliseconds(30);
        });
        await using var provider = services.BuildServiceProvider();
        var worker = provider.GetRequiredService<IEnumerable<IHostedService>>().OfType<JobWorker>().Single();
        var store = provider.GetRequiredService<IJobStore>();
        var job = await provider.GetRequiredService<IJobClient>().EnqueueAsync(new LostLeaseJob());

        await worker.StartAsync(CancellationToken.None);
        await WaitForStatusAsync(store, job.Id, JobStatus.Processing);
        await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        await state.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(JobStatus.DeadLettered, (await store.GetAsync(job.Id))!.Status);
        Assert.Equal(JobFailureKind.Cancellation, (await store.GetAsync(job.Id))!.FailureKind);
    }

    private static async Task WaitForStatusAsync(IJobStore store, Guid id, JobStatus status)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while ((await store.GetAsync(id, timeout.Token))?.Status != status)
        {
            await Task.Delay(5, timeout.Token);
        }
    }

    public sealed record TestJob(string Value);

    public sealed record RetryJob;

    public sealed record PermanentJob;

    public sealed record TimeoutJob;

    public sealed record DrainJob;

    public sealed record NonCooperativeTimeoutJob;

    public sealed record LostLeaseJob;

    public sealed class RetryHandler(RetryState state) : IJobHandler<RetryJob>
    {
        public Task HandleAsync(RetryJob job, CancellationToken cancellationToken)
        {
            if (state.IncrementAttempts() == 1) throw new IOException("temporary");
            state.Succeeded.TrySetResult();
            return Task.CompletedTask;
        }
    }

    public sealed class PermanentHandler : IJobHandler<PermanentJob>
    {
        public Task HandleAsync(PermanentJob job, CancellationToken cancellationToken) =>
            throw new JobExecutionException(JobFailureKind.Permanent, "invalid payload");
    }

    public sealed class TimeoutHandler(TimeoutState state) : IJobHandler<TimeoutJob>
    {
        public async Task HandleAsync(TimeoutJob job, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                state.Canceled.TrySetResult();
                throw;
            }
        }
    }

    public sealed class DrainHandler(DrainState state) : IJobHandler<DrainJob>
    {
        public async Task HandleAsync(DrainJob job, CancellationToken cancellationToken)
        {
            state.Started.TrySetResult();
            await state.Release.Task.WaitAsync(cancellationToken);
        }
    }

    public sealed class NonCooperativeTimeoutHandler(NonCooperativeTimeoutState state) : IJobHandler<NonCooperativeTimeoutJob>
    {
        public async Task HandleAsync(NonCooperativeTimeoutJob job, CancellationToken cancellationToken)
        {
            var attempt = state.IncrementStarts();
            if (attempt == 1)
            {
                await Task.Delay(50, CancellationToken.None);
                state.TimedOut.TrySetResult();
                await state.Release.Task;
                return;
            }

            state.Succeeded.TrySetResult();
        }
    }

    public sealed class LostLeaseHandler(LostLeaseState state) : IJobHandler<LostLeaseJob>
    {
        public async Task HandleAsync(LostLeaseJob job, CancellationToken cancellationToken)
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { state.Canceled.TrySetResult(); throw; }
        }
    }

    public sealed class RetryState
    {
        private int attempts;

        public TaskCompletionSource Succeeded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int IncrementAttempts() => Interlocked.Increment(ref attempts);
    }

    public sealed class TimeoutState
    {
        public TaskCompletionSource Canceled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public sealed class DrainState
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public sealed class NonCooperativeTimeoutState
    {
        private int starts;
        public int Starts => Volatile.Read(ref starts);
        public TaskCompletionSource TimedOut { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Succeeded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int IncrementStarts() => Interlocked.Increment(ref starts);
    }

    public sealed class LostLeaseState
    {
        public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class LeaseRejectingStore(IJobStore inner) : IJobStore
    {
        public int LifecycleWrites { get; private set; }
        public ValueTask<Job> EnqueueAsync(string type, string payload, DateTimeOffset? scheduledAt = null, CancellationToken cancellationToken = default) => inner.EnqueueAsync(type, payload, scheduledAt, cancellationToken);
        public ValueTask<Job> EnqueueAsync(string type, string payload, JobEnqueueOptions options, CancellationToken cancellationToken = default) => inner.EnqueueAsync(type, payload, options, cancellationToken);
        public ValueTask<JobLease?> ClaimAsync(TimeSpan leaseDuration, CancellationToken cancellationToken = default) => inner.ClaimAsync(leaseDuration, cancellationToken);
        public ValueTask<JobLease?> ClaimAsync(TimeSpan leaseDuration, IReadOnlyCollection<string> queues, CancellationToken cancellationToken = default) => inner.ClaimAsync(leaseDuration, queues, cancellationToken);
        public ValueTask<bool> CompleteAsync(JobLease lease, CancellationToken cancellationToken = default) { LifecycleWrites++; return inner.CompleteAsync(lease, cancellationToken); }
        public ValueTask<bool> FailAsync(JobLease lease, string failure, CancellationToken cancellationToken = default) { LifecycleWrites++; return inner.FailAsync(lease, failure, cancellationToken); }
        public ValueTask<bool> RetryAsync(JobLease lease, JobFailure failure, DateTimeOffset retryAt, CancellationToken cancellationToken = default) { LifecycleWrites++; return inner.RetryAsync(lease, failure, retryAt, cancellationToken); }
        public ValueTask<bool> DeadLetterAsync(JobLease lease, JobFailure failure, CancellationToken cancellationToken = default) { LifecycleWrites++; return inner.DeadLetterAsync(lease, failure, cancellationToken); }
        public ValueTask<JobLease?> RenewLeaseAsync(JobLease lease, TimeSpan leaseDuration, CancellationToken cancellationToken = default) => ValueTask.FromResult<JobLease?>(null);
        public ValueTask<IReadOnlyList<Job>> GetDeadLettersAsync(CancellationToken cancellationToken = default) => inner.GetDeadLettersAsync(cancellationToken);
        public ValueTask<bool> ReplayDeadLetterAsync(Guid jobId, CancellationToken cancellationToken = default) => inner.ReplayDeadLetterAsync(jobId, cancellationToken);
        public ValueTask<int> PurgeDeadLettersAsync(DateTimeOffset completedBefore, CancellationToken cancellationToken = default) => inner.PurgeDeadLettersAsync(completedBefore, cancellationToken);
        public ValueTask<DeadLetterMaintenanceResult> PurgeDeadLettersBatchAsync(DateTimeOffset completedBefore, int batchSize, CancellationToken cancellationToken = default) => inner.PurgeDeadLettersBatchAsync(completedBefore, batchSize, cancellationToken);
        public ValueTask<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken = default) => inner.CancelAsync(jobId, cancellationToken);
        public ValueTask<Job?> GetAsync(Guid jobId, CancellationToken cancellationToken = default) => inner.GetAsync(jobId, cancellationToken);
        public ValueTask<IReadOnlyList<Job>> ListAsync(JobQuery query, CancellationToken cancellationToken = default) => inner.ListAsync(query, cancellationToken);
    }

    public sealed class TestJobHandler(ProcessingState state) : IJobHandler<TestJob>
    {
        public Task HandleAsync(TestJob job, CancellationToken cancellationToken)
        {
            Assert.False(string.IsNullOrWhiteSpace(job.Value));
            if (state.Increment() == 2)
            {
                state.Handled.TrySetResult();
            }

            return Task.CompletedTask;
        }
    }

    public sealed class ProcessingState
    {
        public TaskCompletionSource Handled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int count;

        public int Increment() => Interlocked.Increment(ref count);
    }
}
