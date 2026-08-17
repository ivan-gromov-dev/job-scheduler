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

    public sealed record TestJob(string Value);

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
