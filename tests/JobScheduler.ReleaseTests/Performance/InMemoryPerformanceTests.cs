using System.Diagnostics;
using JobScheduler.Core.Jobs;

namespace JobScheduler.ReleaseTests.Performance;

public sealed class InMemoryPerformanceTests
{
    [Fact]
    public async Task EnqueueAndCompleteTenThousandJobsWithinBudget()
    {
        var store = new InMemoryJobStore(TimeProvider.System, new JobQueueOptions());
        var stopwatch = Stopwatch.StartNew();

        for (var index = 0; index < 10_000; index++)
        {
            await store.EnqueueAsync("release.performance", "{}");
        }

        for (var index = 0; index < 10_000; index++)
        {
            var lease = await store.ClaimAsync(TimeSpan.FromMinutes(1));
            Assert.NotNull(lease);
            Assert.True(await store.CompleteAsync(lease));
        }

        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"Elapsed: {stopwatch.Elapsed}");
    }
}
