using JobScheduler.Core.Jobs;
using JobScheduler.PostgreSql;
using Npgsql;
using System.Globalization;
using Xunit.Sdk;

namespace JobScheduler.PostgreSql.IntegrationTests;

public sealed class PostgreSqlJobStoreIntegrationTests
{
    [Fact]
    public async Task MigrationContentionCrashRecoveryAndRestartPreserveJobs()
    {
        var connectionString = Environment.GetEnvironmentVariable("JOB_SCHEDULER_POSTGRES_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw SkipException.ForSkip("Set JOB_SCHEDULER_POSTGRES_TEST_CONNECTION_STRING to run PostgreSQL integration tests.");
        }
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

    private static async Task ResetAsync(NpgsqlDataSource dataSource)
    {
        await using var command = dataSource.CreateCommand("DROP TABLE IF EXISTS job_scheduler_jobs; DROP TABLE IF EXISTS \"__EFMigrationsHistory\";");
        await command.ExecuteNonQueryAsync();
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now = now.Add(duration);
    }
}
