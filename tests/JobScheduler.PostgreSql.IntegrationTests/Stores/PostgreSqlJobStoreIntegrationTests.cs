using JobScheduler.Core.Jobs;
using JobScheduler.Core.Scheduling;
using JobScheduler.PostgreSql;
using Npgsql;
using System.Globalization;

namespace JobScheduler.PostgreSql.IntegrationTests;

public sealed class PostgreSqlJobStoreIntegrationTests
{
    [Fact]
    public async Task MigrationContentionCrashRecoveryAndRestartPreserveJobs()
    {
        var connectionString = Environment.GetEnvironmentVariable("JOB_SCHEDULER_POSTGRES_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Set JOB_SCHEDULER_POSTGRES_TEST_CONNECTION_STRING to run PostgreSQL integration tests.");
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await ResetAsync(dataSource);
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var migrators = Enumerable.Range(0, 4).Select(_ => new PostgreSqlMigrator(dataSource)).ToArray();
        await Task.WhenAll(migrators.Select(x => x.MigrateAsync()));
        await Task.WhenAll(migrators.Select(x => x.MigrateAsync()));
        var options = new PostgreSqlJobStoreOptions { ConnectionString = connectionString, AutoMigrate = false };
        using var first = new PostgreSqlJobStore(dataSource, options, migrators[0], clock);
        using var second = new PostgreSqlJobStore(dataSource, options, migrators[1], clock);

        Assert.Null(await first.GetAsync(Guid.NewGuid()));
        var low = await first.EnqueueAsync("priority", "low", new JobEnqueueOptions { Queue = "operations", Priority = 1, MaxQueueDepth = 2 });
        var high = await first.EnqueueAsync("priority", "high", new JobEnqueueOptions { Queue = "operations", Priority = 9, CorrelationId = "trace-1", MaxQueueDepth = 2 });
        await Assert.ThrowsAsync<QueueFullException>(() => first.EnqueueAsync("priority", "full", new JobEnqueueOptions { Queue = "operations", MaxQueueDepth = 2 }).AsTask());
        var highLease = await first.ClaimAsync(TimeSpan.FromMinutes(1), ["operations"]);
        Assert.Equal(high.Id, highLease!.Job.Id);
        Assert.Equal("trace-1", highLease.Job.CorrelationId);
        Assert.True(await first.CompleteAsync(highLease));
        var listed = await first.ListAsync(new JobQuery { Queue = "operations", Status = JobStatus.Pending });
        Assert.Equal(low.Id, Assert.Single(listed).Id);
        var lowLease = await first.ClaimAsync(TimeSpan.FromMinutes(1), ["operations"]);
        Assert.True(await first.CompleteAsync(lowLease!));
        var duplicate = await first.EnqueueAsync("deduplicated", "first", new JobEnqueueOptions { DeduplicationKey = "one" });
        var coalesced = await second.EnqueueAsync("deduplicated", "second", new JobEnqueueOptions { DeduplicationKey = "one" });
        Assert.Equal(duplicate.Id, coalesced.Id);
        var duplicateLease = await first.ClaimAsync(TimeSpan.FromMinutes(1));
        var renewed = await second.RenewLeaseAsync(duplicateLease!, TimeSpan.FromMinutes(2));
        Assert.NotNull(renewed);
        Assert.True(await first.CompleteAsync(renewed!));

        var canceled = await first.EnqueueAsync("cancel", "payload");
        Assert.True(await second.CancelAsync(canceled.Id));
        Assert.False(await second.CancelAsync(canceled.Id));
        Assert.Equal(JobStatus.Canceled, (await first.GetAsync(canceled.Id))!.Status);

        var delayed = await first.EnqueueAsync("delayed", "payload", clock.GetUtcNow().AddHours(1));
        Assert.Null(await first.ClaimAsync(TimeSpan.FromMinutes(1)));
        clock.Advance(TimeSpan.FromHours(1));
        var delayedLease = await second.ClaimAsync(TimeSpan.FromMinutes(1));
        Assert.Equal(delayed.Id, delayedLease!.Job.Id);
        Assert.True(await first.FailAsync(delayedLease, "permanent"));
        Assert.Equal(JobStatus.Failed, (await second.GetAsync(delayed.Id))!.Status);

        var jobs = await Task.WhenAll(Enumerable.Range(0, 24).Select(x => first.EnqueueAsync("test", x.ToString(CultureInfo.InvariantCulture)).AsTask()));
        var claims = await Task.WhenAll(Enumerable.Range(0, jobs.Length).Select(x => (x % 2 == 0 ? first : second).ClaimAsync(TimeSpan.FromMinutes(1)).AsTask()));
        Assert.Equal(jobs.Length, claims.Where(x => x is not null).Select(x => x!.Job.Id).Distinct().Count());
        Assert.Null(await first.ClaimAsync(TimeSpan.FromMinutes(1)));
        var abandoned = claims[0]!;
        foreach (var lease in claims.Skip(1)) Assert.True(await first.CompleteAsync(lease!));
        clock.Advance(TimeSpan.FromMinutes(2));
        var recovered = await second.ClaimAsync(TimeSpan.FromMinutes(1));
        Assert.Equal(abandoned.Job.Id, recovered!.Job.Id);
        Assert.Equal(2, recovered.Job.Attempt);
        Assert.False(await first.CompleteAsync(abandoned));
        Assert.True(await second.RetryAsync(recovered, new JobFailure(JobFailureKind.Transient, "retry"), clock.GetUtcNow()));
        var afterRestart = await first.ClaimAsync(TimeSpan.FromMinutes(1));
        Assert.Equal(recovered.Job.Id, afterRestart!.Job.Id);
        Assert.True(await first.DeadLetterAsync(afterRestart, new JobFailure(JobFailureKind.Permanent, "bad")));
        Assert.Single(await second.GetDeadLettersAsync());
        Assert.True(await second.ReplayDeadLetterAsync(afterRestart.Job.Id));
        var replayed = await first.ClaimAsync(TimeSpan.FromMinutes(1));
        Assert.NotNull(replayed);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            second.RenewLeaseAsync(replayed!, TimeSpan.FromMinutes(-1)).AsTask());
        Assert.True(await first.DeadLetterAsync(replayed, new JobFailure(JobFailureKind.Permanent, "again")));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, await second.PurgeDeadLettersAsync(clock.GetUtcNow()));
        Assert.Empty(await first.GetDeadLettersAsync());

        await ResetAsync(dataSource);
        using var autoMigrating = new PostgreSqlJobStore(dataSource,
            new PostgreSqlJobStoreOptions { ConnectionString = connectionString }, migrators[2], clock);
        Assert.NotNull(await autoMigrating.EnqueueAsync("auto", "migration"));
    }

    [Fact]
    public async Task ConcurrentSchedulersMaterializeEachOccurrenceOnceAndPersistLifecycle()
    {
        var connectionString = Environment.GetEnvironmentVariable("JOB_SCHEDULER_POSTGRES_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Set JOB_SCHEDULER_POSTGRES_TEST_CONNECTION_STRING to run PostgreSQL integration tests.");
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await ResetAsync(dataSource);
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero));
        var migrator = new PostgreSqlMigrator(dataSource);
        await migrator.MigrateAsync();
        var options = new PostgreSqlJobStoreOptions { ConnectionString = connectionString, AutoMigrate = false };
        var first = new PostgreSqlScheduleStore(dataSource, options, migrator, clock);
        var second = new PostgreSqlScheduleStore(dataSource, options, migrator, clock);
        using var jobs = new PostgreSqlJobStore(dataSource, options, migrator, clock);
        var schedule = await first.CreateAsync("hourly", "{}", new ScheduleOptions { CronExpression = "1 * * * *", MisfirePolicy = MisfirePolicy.CatchUp });

        var counts = await Task.WhenAll(first.MaterializeDueAsync(clock.GetUtcNow().AddHours(2).AddMinutes(1)).AsTask(), second.MaterializeDueAsync(clock.GetUtcNow().AddHours(2).AddMinutes(1)).AsTask());
        Assert.Equal(3, counts.Sum());
        Assert.True(await first.PauseAsync(schedule.Id));
        Assert.True(await second.ResumeAsync(schedule.Id));
        Assert.NotNull(await first.UpdateAsync(schedule.Id, new ScheduleOptions { RunAt = clock.GetUtcNow().AddDays(1) }));
        Assert.True(await second.DeleteAsync(schedule.Id));
        Assert.Null(await first.GetAsync(schedule.Id));

        clock.Advance(TimeSpan.FromHours(3));
        var claimed = new List<JobLease>();
        while (await jobs.ClaimAsync(TimeSpan.FromMinutes(1)) is { } lease) { claimed.Add(lease); await jobs.CompleteAsync(lease); }
        Assert.Equal(3, claimed.Count);
    }

    private static async Task ResetAsync(NpgsqlDataSource dataSource)
    {
        await using var command = dataSource.CreateCommand("DROP TABLE IF EXISTS job_scheduler_schedules; DROP TABLE IF EXISTS job_scheduler_jobs; DROP TABLE IF EXISTS \"__EFMigrationsHistory\";");
        await command.ExecuteNonQueryAsync();
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now = now.Add(duration);
    }
}
