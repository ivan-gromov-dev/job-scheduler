using JobScheduler.Core.Jobs;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JobScheduler.PostgreSql;

public sealed class PostgreSqlJobStore(NpgsqlDataSource dataSource, PostgreSqlJobStoreOptions options, PostgreSqlMigrator migrator, TimeProvider timeProvider) : IJobStore, IDisposable
{
    private readonly SemaphoreSlim migrationLock = new(1, 1);
    private volatile bool migrated;

    public ValueTask<Job> EnqueueAsync(string type, string payload, DateTimeOffset? scheduledAt = null, CancellationToken cancellationToken = default) => EnqueueAsync(type, payload, new JobEnqueueOptions { ScheduledAt = scheduledAt }, cancellationToken);

    public async ValueTask<Job> EnqueueAsync(string type, string payload, JobEnqueueOptions scheduledAt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type); ArgumentNullException.ThrowIfNull(payload); ArgumentNullException.ThrowIfNull(scheduledAt);
        await EnsureMigratedAsync(cancellationToken);
        var job = Job.Create(type, payload, timeProvider.GetUtcNow(), scheduledAt.ScheduledAt, string.IsNullOrWhiteSpace(scheduledAt.DeduplicationKey) ? null : scheduledAt.DeduplicationKey);
        await using var context = new JobSchedulerDbContext(dataSource);
        if (job.DeduplicationKey is not null)
        {
            var existing = await FindDeduplicatedAsync(context, job.Type, job.DeduplicationKey, cancellationToken);
            if (existing is not null) return existing.ToJob();
        }
        context.Jobs.Add(JobEntity.FromJob(job));
        try { await context.SaveChangesAsync(cancellationToken); return job; }
        catch (DbUpdateException exception) when (job.DeduplicationKey is not null && exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            context.ChangeTracker.Clear();
            return (await FindDeduplicatedAsync(context, job.Type, job.DeduplicationKey, cancellationToken))!.ToJob();
        }
    }

    public async ValueTask<JobLease?> ClaimAsync(TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseDuration, TimeSpan.Zero); await EnsureMigratedAsync(cancellationToken);
        var now = timeProvider.GetUtcNow(); var token = Guid.NewGuid(); var expiresAt = now.Add(leaseDuration);
        const string sql = """
            WITH candidate AS (SELECT id FROM job_scheduler_jobs
              WHERE (status=0 AND scheduled_at <= $1) OR (status=1 AND lease_expires_at <= $1)
              ORDER BY CASE WHEN status=0 THEN scheduled_at ELSE lease_expires_at END, enqueued_at, id
              FOR UPDATE SKIP LOCKED LIMIT 1)
            UPDATE job_scheduler_jobs j SET status=1,attempt=attempt+1,failure=NULL,failure_kind=NULL,
              completed_at=NULL,lease_token=$2,lease_expires_at=$3,row_version=row_version+1
            FROM candidate WHERE j.id=candidate.id
            RETURNING j.id,j.type,j.payload,j.enqueued_at,j.scheduled_at,j.status,j.attempt,j.failure,j.failure_kind,j.deduplication_key,j.completed_at
            """;
        await using var command = dataSource.CreateCommand(sql); command.Parameters.AddWithValue(now); command.Parameters.AddWithValue(token); command.Parameters.AddWithValue(expiresAt);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new JobLease(ReadJob(reader), token, expiresAt) : null;
    }

    public ValueTask<bool> CompleteAsync(JobLease lease, CancellationToken cancellationToken = default) => FinishAsync(lease, JobStatus.Succeeded, null, null, cancellationToken);
    public ValueTask<bool> FailAsync(JobLease lease, string failure, CancellationToken cancellationToken = default) { ArgumentException.ThrowIfNullOrWhiteSpace(failure); return FinishAsync(lease, JobStatus.Failed, new JobFailure(JobFailureKind.Permanent, failure), null, cancellationToken); }
    public ValueTask<bool> RetryAsync(JobLease lease, JobFailure failure, DateTimeOffset retryAt, CancellationToken cancellationToken = default) => FinishAsync(lease, JobStatus.Pending, failure, retryAt.ToUniversalTime(), cancellationToken);
    public ValueTask<bool> DeadLetterAsync(JobLease lease, JobFailure failure, CancellationToken cancellationToken = default) => FinishAsync(lease, JobStatus.DeadLettered, failure, null, cancellationToken);

    public async ValueTask<JobLease?> RenewLeaseAsync(JobLease lease, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease); ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseDuration, TimeSpan.Zero); await EnsureMigratedAsync(cancellationToken);
        var now = timeProvider.GetUtcNow(); var expiresAt = now.Add(leaseDuration);
        const string sql = "UPDATE job_scheduler_jobs SET lease_expires_at=$1,row_version=row_version+1 WHERE id=$2 AND status=1 AND lease_token=$3 AND lease_expires_at>$4";
        await using var command = dataSource.CreateCommand(sql); command.Parameters.AddWithValue(expiresAt); command.Parameters.AddWithValue(lease.Job.Id); command.Parameters.AddWithValue(lease.Token); command.Parameters.AddWithValue(now);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1 ? new JobLease(lease.Job, lease.Token, expiresAt) : null;
    }

    public async ValueTask<IReadOnlyList<Job>> GetDeadLettersAsync(CancellationToken cancellationToken = default)
    {
        await EnsureMigratedAsync(cancellationToken); await using var context = new JobSchedulerDbContext(dataSource);
        var entities = await context.Jobs.AsNoTracking().Where(x => x.Status == JobStatus.DeadLettered).OrderBy(x => x.CompletedAt).ThenBy(x => x.Id).ToArrayAsync(cancellationToken);
        return entities.Select(x => x.ToJob()).ToArray();
    }

    public async ValueTask<bool> ReplayDeadLetterAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        await EnsureMigratedAsync(cancellationToken); await using var context = new JobSchedulerDbContext(dataSource); var now = timeProvider.GetUtcNow();
        return await context.Jobs.Where(x => x.Id == jobId && x.Status == JobStatus.DeadLettered).ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, JobStatus.Pending).SetProperty(x => x.ScheduledAt, now).SetProperty(x => x.Attempt, 0).SetProperty(x => x.Failure, (string?)null).SetProperty(x => x.FailureKind, (JobFailureKind?)null).SetProperty(x => x.CompletedAt, (DateTimeOffset?)null).SetProperty(x => x.RowVersion, x => x.RowVersion + 1), cancellationToken) == 1;
    }

    public async ValueTask<int> PurgeDeadLettersAsync(DateTimeOffset completedBefore, CancellationToken cancellationToken = default)
    {
        await EnsureMigratedAsync(cancellationToken); await using var context = new JobSchedulerDbContext(dataSource); var cutoff = completedBefore.ToUniversalTime();
        return await context.Jobs.Where(x => x.Status == JobStatus.DeadLettered && x.CompletedAt < cutoff).ExecuteDeleteAsync(cancellationToken);
    }

    public async ValueTask<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        await EnsureMigratedAsync(cancellationToken); await using var context = new JobSchedulerDbContext(dataSource); var now = timeProvider.GetUtcNow();
        return await context.Jobs.Where(x => x.Id == jobId && x.Status == JobStatus.Pending).ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, JobStatus.Canceled).SetProperty(x => x.CompletedAt, now).SetProperty(x => x.RowVersion, x => x.RowVersion + 1), cancellationToken) == 1;
    }

    public async ValueTask<Job?> GetAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        await EnsureMigratedAsync(cancellationToken); await using var context = new JobSchedulerDbContext(dataSource);
        return (await context.Jobs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == jobId, cancellationToken))?.ToJob();
    }

    private async ValueTask<bool> FinishAsync(JobLease lease, JobStatus status, JobFailure? failure, DateTimeOffset? scheduledAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease); if (failure is null && status is JobStatus.Pending or JobStatus.DeadLettered) throw new ArgumentNullException(nameof(failure));
        await EnsureMigratedAsync(cancellationToken); DateTimeOffset? completedAt = status == JobStatus.Pending ? null : timeProvider.GetUtcNow();
        const string sql = "UPDATE job_scheduler_jobs SET status=$1,failure=$2,failure_kind=$3,scheduled_at=COALESCE($4,scheduled_at),completed_at=$5,lease_token=NULL,lease_expires_at=NULL,row_version=row_version+1 WHERE id=$6 AND status=1 AND lease_token=$7";
        await using var command = dataSource.CreateCommand(sql); command.Parameters.AddWithValue((short)status); command.Parameters.AddWithValue((object?)failure?.Message ?? DBNull.Value); command.Parameters.AddWithValue(failure is null ? DBNull.Value : (short)failure.Kind); command.Parameters.AddWithValue((object?)scheduledAt ?? DBNull.Value); command.Parameters.AddWithValue((object?)completedAt ?? DBNull.Value); command.Parameters.AddWithValue(lease.Job.Id); command.Parameters.AddWithValue(lease.Token);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private async ValueTask EnsureMigratedAsync(CancellationToken cancellationToken)
    {
        if (migrated || !options.AutoMigrate) return; await migrationLock.WaitAsync(cancellationToken);
        try { if (!migrated) { await migrator.MigrateAsync(cancellationToken); migrated = true; } } finally { migrationLock.Release(); }
    }

    private static Task<JobEntity?> FindDeduplicatedAsync(JobSchedulerDbContext context, string type, string key, CancellationToken cancellationToken) => context.Jobs.AsNoTracking().SingleOrDefaultAsync(x => x.Type == type && x.DeduplicationKey == key && (x.Status == JobStatus.Pending || x.Status == JobStatus.Processing || x.Status == JobStatus.Succeeded), cancellationToken);

    private static Job ReadJob(NpgsqlDataReader reader) => new() { Id = reader.GetGuid(0), Type = reader.GetString(1), Payload = reader.GetString(2), EnqueuedAt = reader.GetFieldValue<DateTimeOffset>(3), ScheduledAt = reader.GetFieldValue<DateTimeOffset>(4), Status = (JobStatus)reader.GetInt16(5), Attempt = reader.GetInt32(6), Failure = reader.IsDBNull(7) ? null : reader.GetString(7), FailureKind = reader.IsDBNull(8) ? null : (JobFailureKind)reader.GetInt16(8), DeduplicationKey = reader.IsDBNull(9) ? null : reader.GetString(9), CompletedAt = reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10) };

    public void Dispose() => migrationLock.Dispose();
}
