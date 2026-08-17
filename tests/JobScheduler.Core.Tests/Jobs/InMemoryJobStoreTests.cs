using JobScheduler.Core.Jobs;

namespace JobScheduler.Core.Tests.Jobs;

public sealed class InMemoryJobStoreTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 8, 17, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ClaimCompletesImmediateJob()
    {
        var store = new InMemoryJobStore(new TestTimeProvider(Start));
        var job = await store.EnqueueAsync("test", "{}");

        var lease = await store.ClaimAsync(TimeSpan.FromMinutes(1));

        Assert.NotNull(lease);
        Assert.Equal(job.Id, lease.Job.Id);
        Assert.Equal(1, lease.Job.Attempt);
        Assert.True(await store.CompleteAsync(lease));
        Assert.Equal(JobStatus.Succeeded, (await store.GetAsync(job.Id))!.Status);
        Assert.Null(await store.ClaimAsync(TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task DelayedJobBecomesClaimableWhenClockReachesSchedule()
    {
        var time = new TestTimeProvider(Start);
        var store = new InMemoryJobStore(time);
        var job = await store.EnqueueAsync("test", "{}", Start.AddMinutes(5));

        Assert.Null(await store.ClaimAsync(TimeSpan.FromMinutes(1)));
        time.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(job.Id, (await store.ClaimAsync(TimeSpan.FromMinutes(1)))!.Job.Id);
    }

    [Fact]
    public async Task ExpiredLeaseIsReclaimedAndStaleLeaseCannotComplete()
    {
        var time = new TestTimeProvider(Start);
        var store = new InMemoryJobStore(time);
        var job = await store.EnqueueAsync("test", "{}");
        var firstLease = await store.ClaimAsync(TimeSpan.FromMinutes(1));
        time.Advance(TimeSpan.FromMinutes(1));

        var secondLease = await store.ClaimAsync(TimeSpan.FromMinutes(1));

        Assert.NotNull(firstLease);
        Assert.NotNull(secondLease);
        Assert.Equal(2, secondLease.Job.Attempt);
        Assert.False(await store.CompleteAsync(firstLease));
        Assert.True(await store.CompleteAsync(secondLease));
        Assert.Equal(JobStatus.Succeeded, (await store.GetAsync(job.Id))!.Status);
    }

    [Fact]
    public async Task FailRecordsFailure()
    {
        var store = new InMemoryJobStore(new TestTimeProvider(Start));
        var job = await store.EnqueueAsync("test", "{}");
        var lease = await store.ClaimAsync(TimeSpan.FromMinutes(1));

        Assert.True(await store.FailAsync(lease!, "broken"));

        var failed = await store.GetAsync(job.Id);
        Assert.Equal(JobStatus.Failed, failed!.Status);
        Assert.Equal("broken", failed.Failure);
    }

    [Fact]
    public async Task CancelOnlyCancelsPendingJob()
    {
        var store = new InMemoryJobStore(new TestTimeProvider(Start));
        var processing = await store.EnqueueAsync("test", "{}");
        var pending = await store.EnqueueAsync("test", "{}");
        await store.ClaimAsync(TimeSpan.FromMinutes(1));

        Assert.False(await store.CancelAsync(processing.Id));
        Assert.True(await store.CancelAsync(pending.Id));
        Assert.False(await store.CancelAsync(pending.Id));
        Assert.Equal(JobStatus.Canceled, (await store.GetAsync(pending.Id))!.Status);
    }

    [Fact]
    public async Task ConcurrentClaimsReturnEachJobOnce()
    {
        var store = new InMemoryJobStore(new TestTimeProvider(Start));
        for (var index = 0; index < 20; index++)
        {
            await store.EnqueueAsync("test", index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var claims = await Task.WhenAll(
            Enumerable.Range(0, 20)
                .Select(_ => store.ClaimAsync(TimeSpan.FromMinutes(1)).AsTask()));

        Assert.Equal(20, claims.Select(lease => lease!.Job.Id).Distinct().Count());
    }

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan duration) => utcNow = utcNow.Add(duration);
    }
}
