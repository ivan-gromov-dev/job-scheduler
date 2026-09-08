using JobScheduler.Core.Jobs;
using Microsoft.Extensions.DependencyInjection;

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

    [Theory]
    [InlineData("complete")]
    [InlineData("fail")]
    [InlineData("retry")]
    [InlineData("dead-letter")]
    public async Task ExpiredLeaseCannotWriteLifecycleTransition(string transition)
    {
        var time = new TestTimeProvider(Start);
        var store = new InMemoryJobStore(time);
        await store.EnqueueAsync("test", "{}");
        var lease = await store.ClaimAsync(TimeSpan.FromMinutes(1));
        time.Advance(TimeSpan.FromMinutes(1));

        var changed = transition switch
        {
            "complete" => await store.CompleteAsync(lease!),
            "fail" => await store.FailAsync(lease!, "failure"),
            "retry" => await store.RetryAsync(lease!, new JobFailure(JobFailureKind.Transient, "failure"), Start),
            "dead-letter" => await store.DeadLetterAsync(lease!, new JobFailure(JobFailureKind.Permanent, "failure")),
            _ => throw new ArgumentOutOfRangeException(nameof(transition)),
        };

        Assert.False(changed);
        Assert.Equal(JobStatus.Processing, (await store.GetAsync(lease!.Job.Id))!.Status);
    }

    [Fact]
    public async Task ExpiredLeaseCannotWriteAnyTerminalOutcomeBeforeReclaim()
    {
        var time = new TestTimeProvider(Start);
        var store = new InMemoryJobStore(time);
        await store.EnqueueAsync("test", "{}");
        var lease = await store.ClaimAsync(TimeSpan.FromMinutes(1));
        time.Advance(TimeSpan.FromMinutes(1));

        Assert.False(await store.CompleteAsync(lease!));
        Assert.False(await store.FailAsync(lease!, "stale"));
        Assert.False(await store.RetryAsync(lease!, new JobFailure(JobFailureKind.Transient, "stale"), Start));
        Assert.False(await store.DeadLetterAsync(lease!, new JobFailure(JobFailureKind.Permanent, "stale")));
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

    [Fact]
    public async Task RetrySchedulesJobAndRecordsClassifiedFailure()
    {
        var time = new TestTimeProvider(Start);
        var store = new InMemoryJobStore(time);
        var job = await store.EnqueueAsync("test", "{}");
        var lease = await store.ClaimAsync(TimeSpan.FromMinutes(1));
        var retryAt = Start.AddMinutes(2);

        Assert.True(await store.RetryAsync(lease!, new JobFailure(JobFailureKind.Transient, "network"), retryAt));
        var retried = await store.GetAsync(job.Id);
        Assert.Equal(JobStatus.Pending, retried!.Status);
        Assert.Equal(JobFailureKind.Transient, retried.FailureKind);
        Assert.Equal(retryAt, retried.ScheduledAt);
        Assert.Null(await store.ClaimAsync(TimeSpan.FromMinutes(1)));
        time.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(2, (await store.ClaimAsync(TimeSpan.FromMinutes(1)))!.Job.Attempt);
    }

    [Fact]
    public async Task LeaseRenewalPreventsEarlyReclaim()
    {
        var time = new TestTimeProvider(Start);
        var store = new InMemoryJobStore(time);
        await store.EnqueueAsync("test", "{}");
        var lease = await store.ClaimAsync(TimeSpan.FromMinutes(1));
        time.Advance(TimeSpan.FromSeconds(30));

        var renewed = await store.RenewLeaseAsync(lease!, TimeSpan.FromMinutes(1));
        time.Advance(TimeSpan.FromSeconds(31));

        Assert.NotNull(renewed);
        Assert.Null(await store.ClaimAsync(TimeSpan.FromMinutes(1)));
        time.Advance(TimeSpan.FromSeconds(29));
        Assert.NotNull(await store.ClaimAsync(TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task DeadLetterCanBeInspectedReplayedAndPurged()
    {
        var time = new TestTimeProvider(Start);
        var store = new InMemoryJobStore(time);
        var first = await store.EnqueueAsync("test", "one");
        var lease = await store.ClaimAsync(TimeSpan.FromMinutes(1));
        await store.DeadLetterAsync(lease!, new JobFailure(JobFailureKind.Permanent, "invalid"));

        var deadLetters = await store.GetDeadLettersAsync();
        Assert.Equal(first.Id, Assert.Single(deadLetters).Id);
        Assert.Equal(Start, deadLetters[0].CompletedAt);
        Assert.True(await store.ReplayDeadLetterAsync(first.Id));
        Assert.Equal(0, (await store.GetAsync(first.Id))!.Attempt);

        var replayLease = await store.ClaimAsync(TimeSpan.FromMinutes(1));
        await store.DeadLetterAsync(replayLease!, new JobFailure(JobFailureKind.Permanent, "invalid"));
        time.Advance(TimeSpan.FromDays(2));
        Assert.Equal(1, await store.PurgeDeadLettersAsync(Start.AddDays(1)));
        Assert.Null(await store.GetAsync(first.Id));
    }

    [Fact]
    public async Task DeadLetterMaintenancePurgesInBoundedBatches()
    {
        var time = new TestTimeProvider(Start);
        var store = new InMemoryJobStore(time);
        for (var index = 0; index < 3; index++)
        {
            await store.EnqueueAsync("test", index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var lease = await store.ClaimAsync(TimeSpan.FromMinutes(1));
            await store.DeadLetterAsync(lease!, new JobFailure(JobFailureKind.Permanent, "invalid"));
        }

        time.Advance(TimeSpan.FromDays(2));
        var first = await store.PurgeDeadLettersBatchAsync(Start.AddDays(1), 2);
        var second = await store.PurgeDeadLettersBatchAsync(Start.AddDays(1), 2);

        Assert.True(first.IsOwner);
        Assert.Equal(2, first.PurgedCount);
        Assert.Equal(1, second.PurgedCount);
        Assert.Empty(await store.GetDeadLettersAsync());
    }

    [Fact]
    public async Task DeduplicationReturnsActiveOrSucceededJobButAllowsReplacementAfterFailure()
    {
        var store = new InMemoryJobStore(new TestTimeProvider(Start));
        var options = new JobEnqueueOptions { DeduplicationKey = "invoice-42" };
        var first = await store.EnqueueAsync("test", "one", options);
        var duplicate = await store.EnqueueAsync("test", "two", options);
        Assert.Equal(first.Id, duplicate.Id);
        Assert.Equal("one", duplicate.Payload);

        var lease = await store.ClaimAsync(TimeSpan.FromMinutes(1));
        await store.FailAsync(lease!, "bad");
        var replacement = await store.EnqueueAsync("test", "two", options);
        Assert.NotEqual(first.Id, replacement.Id);
    }

    [Fact]
    public async Task ClaimsHighestPriorityWithinSelectedQueue()
    {
        var store = new InMemoryJobStore(new TestTimeProvider(Start));
        var other = await store.EnqueueAsync("test", "other", new JobEnqueueOptions { Queue = "slow", Priority = 100 });
        var low = await store.EnqueueAsync("test", "low", new JobEnqueueOptions { Queue = "fast", Priority = 1 });
        var high = await store.EnqueueAsync("test", "high", new JobEnqueueOptions
        {
            Queue = "fast",
            Priority = 10,
            CorrelationId = "request-42",
        });

        var lease = await store.ClaimAsync(TimeSpan.FromMinutes(1), ["fast"]);

        Assert.Equal(high.Id, lease!.Job.Id);
        Assert.Equal("request-42", lease.Job.CorrelationId);
        Assert.NotEqual(other.Id, lease.Job.Id);
        Assert.NotEqual(low.Id, lease.Job.Id);
    }

    [Fact]
    public async Task QueueCapacityAppliesBackpressureAndReleasesAfterCompletion()
    {
        var queues = new JobQueueOptions();
        queues.Capacities["bounded"] = 1;
        var store = new InMemoryJobStore(new TestTimeProvider(Start), queues);
        var options = new JobEnqueueOptions { Queue = "bounded" };
        await store.EnqueueAsync("test", "first", options);

        var exception = await Assert.ThrowsAsync<QueueFullException>(() =>
            store.EnqueueAsync("test", "second", options).AsTask());
        Assert.Equal("bounded", exception.Queue);
        Assert.Equal(1, exception.Capacity);

        var lease = await store.ClaimAsync(TimeSpan.FromMinutes(1));
        await store.CompleteAsync(lease!);
        Assert.NotNull(await store.EnqueueAsync("test", "second", options));
    }

    [Fact]
    public async Task AdministrationListsFiltersCancelsAndReplays()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddJobScheduler();
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IJobStore>();
        var administration = provider.GetRequiredService<IJobAdministration>();
        var pending = await store.EnqueueAsync("test", "{}", new JobEnqueueOptions { Queue = "admin" });
        var dead = await store.EnqueueAsync("test", "dead", new JobEnqueueOptions { Queue = "admin" });
        var lease = await store.ClaimAsync(TimeSpan.FromMinutes(1), ["admin"]);
        Assert.Equal(pending.Id, lease!.Job.Id);
        await store.DeadLetterAsync(lease, new JobFailure(JobFailureKind.Permanent, "bad"));

        Assert.Single(await administration.ListAsync(new JobQuery { Queue = "admin", Status = JobStatus.DeadLettered }));
        Assert.Equal(dead.Id, (await administration.GetAsync(dead.Id))!.Id);
        Assert.True(await administration.CancelAsync(dead.Id));
        Assert.True(await administration.ReplayAsync(pending.Id));
    }

    [Fact]
    public async Task AdministrationPagesWithOperationalFiltersAndBulkControls()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<TimeProvider>(new TestTimeProvider(Start));
        services.AddJobScheduler();
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IJobStore>();
        var administration = provider.GetRequiredService<IJobAdministration>();
        var first = await store.EnqueueAsync("invoice", "1", new JobEnqueueOptions { Queue = "ops", CorrelationId = "batch-7" });
        var second = await store.EnqueueAsync("invoice", "2", new JobEnqueueOptions { Queue = "ops", CorrelationId = "batch-7" });
        await store.EnqueueAsync("email", "3");

        var page = await administration.ListPageAsync(new JobQuery { Type = "invoice", CorrelationId = "batch-7", EnqueuedFrom = Start, EnqueuedThrough = Start, Limit = 1 });
        var next = await administration.ListPageAsync(new JobQuery { Type = "invoice", CorrelationId = "batch-7", Cursor = page.NextCursor, Limit = 1 });

        Assert.Single(page.Items);
        Assert.Single(next.Items);
        Assert.NotEqual(page.Items[0].Id, next.Items[0].Id);
        Assert.Equal(2, await administration.CancelAsync([first.Id, second.Id, Guid.NewGuid()]));
    }

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan duration) => utcNow = utcNow.Add(duration);
    }
}
