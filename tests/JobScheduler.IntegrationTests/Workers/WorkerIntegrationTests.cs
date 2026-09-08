using JobScheduler.Core.Handlers;
using JobScheduler.Core.Jobs;
using JobScheduler.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
        var hostedService = provider.GetRequiredService<IEnumerable<IHostedService>>().Single();
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
        var worker = provider.GetRequiredService<IEnumerable<IHostedService>>().Single();
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
        var worker = provider.GetRequiredService<IEnumerable<IHostedService>>().Single();
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
        var worker = provider.GetRequiredService<IEnumerable<IHostedService>>().Single();
        var store = provider.GetRequiredService<IJobStore>();
        var job = await provider.GetRequiredService<IJobClient>().EnqueueAsync(new TimeoutJob());

        await worker.StartAsync(CancellationToken.None);
        await WaitForStatusAsync(store, job.Id, JobStatus.DeadLettered);
        await state.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(JobFailureKind.Timeout, (await store.GetAsync(job.Id))!.FailureKind);
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
