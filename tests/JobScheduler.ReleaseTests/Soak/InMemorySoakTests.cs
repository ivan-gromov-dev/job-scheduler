using JobScheduler.Core.Jobs;

namespace JobScheduler.ReleaseTests.Soak;

public sealed class InMemorySoakTests
{
    [Fact]
    public async Task ConcurrentProducersAndConsumersDoNotLoseAcceptedJobs()
    {
        const int jobCount = 2_000;
        var store = new InMemoryJobStore(TimeProvider.System, new JobQueueOptions());

        await Parallel.ForEachAsync(
            Enumerable.Range(0, jobCount),
            async (_, cancellationToken) =>
                await store.EnqueueAsync("release.soak", "{}", cancellationToken: cancellationToken));

        var completed = 0;
        await Parallel.ForEachAsync(
            Enumerable.Range(0, 8),
            async (_, cancellationToken) =>
            {
                while (Volatile.Read(ref completed) < jobCount)
                {
                    var lease = await store.ClaimAsync(TimeSpan.FromMinutes(1), cancellationToken);
                    if (lease is null)
                    {
                        await Task.Yield();
                        continue;
                    }

                    Assert.True(await store.CompleteAsync(lease, cancellationToken));
                    Interlocked.Increment(ref completed);
                }
            });

        Assert.Equal(jobCount, completed);
        Assert.Empty(await store.ListAsync(new JobQuery { Status = JobStatus.Pending }));
    }
}
