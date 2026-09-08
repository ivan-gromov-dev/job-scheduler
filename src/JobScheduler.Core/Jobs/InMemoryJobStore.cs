namespace JobScheduler.Core.Jobs;

public sealed class InMemoryJobStore(TimeProvider timeProvider) : IJobStore
{
    private readonly Lock sync = new();
    private readonly Dictionary<Guid, StoredJob> jobs = [];

    public ValueTask<Job> EnqueueAsync(
        string type,
        string payload,
        DateTimeOffset? scheduledAt = null,
        CancellationToken cancellationToken = default) =>
        EnqueueAsync(type, payload, new JobEnqueueOptions { ScheduledAt = scheduledAt }, cancellationToken);

    public ValueTask<Job> EnqueueAsync(
        string type,
        string payload,
        JobEnqueueOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        var now = timeProvider.GetUtcNow();
        var key = string.IsNullOrWhiteSpace(options.DeduplicationKey) ? null : options.DeduplicationKey;
        lock (sync)
        {
            if (key is not null)
            {
                var existing = jobs.Values.FirstOrDefault(candidate =>
                    candidate.Job.Type == type && candidate.Job.DeduplicationKey == key &&
                    candidate.Job.Status is JobStatus.Pending or JobStatus.Processing or JobStatus.Succeeded);
                if (existing is not null)
                {
                    return ValueTask.FromResult(existing.Job);
                }
            }

            var job = Job.Create(type, payload, now, options.ScheduledAt, key);
            jobs.Add(job.Id, new StoredJob(job));
            return ValueTask.FromResult(job);
        }
    }

    public ValueTask<JobLease?> ClaimAsync(
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseDuration, TimeSpan.Zero);
        var now = timeProvider.GetUtcNow();

        lock (sync)
        {
            var stored = jobs.Values
                .Where(candidate => IsClaimable(candidate, now))
                .OrderBy(candidate => candidate.Job.ScheduledAt)
                .ThenBy(candidate => candidate.Job.EnqueuedAt)
                .FirstOrDefault();
            if (stored is null)
            {
                return ValueTask.FromResult<JobLease?>(null);
            }

            var token = Guid.NewGuid();
            var expiresAt = now.Add(leaseDuration);
            stored.Job = stored.Job with
            {
                Status = JobStatus.Processing,
                Attempt = stored.Job.Attempt + 1,
                Failure = null,
            };
            stored.LeaseToken = token;
            stored.LeaseExpiresAt = expiresAt;
            return ValueTask.FromResult<JobLease?>(new JobLease(stored.Job, token, expiresAt));
        }
    }

    public ValueTask<bool> CompleteAsync(
        JobLease lease,
        CancellationToken cancellationToken = default) =>
        FinishAsync(lease, JobStatus.Succeeded, null, cancellationToken);

    public ValueTask<bool> FailAsync(
        JobLease lease,
        string failure,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failure);
        return FinishAsync(lease, JobStatus.Failed, new JobFailure(JobFailureKind.Permanent, failure), cancellationToken);
    }

    public ValueTask<bool> RetryAsync(JobLease lease, JobFailure failure, DateTimeOffset retryAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return FinishAsync(lease, JobStatus.Pending, failure, cancellationToken, retryAt.ToUniversalTime());
    }

    public ValueTask<bool> DeadLetterAsync(JobLease lease, JobFailure failure, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return FinishAsync(lease, JobStatus.DeadLettered, failure, cancellationToken);
    }

    public ValueTask<JobLease?> RenewLeaseAsync(JobLease lease, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseDuration, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            if (!jobs.TryGetValue(lease.Job.Id, out var stored) || stored.Job.Status != JobStatus.Processing ||
                stored.LeaseToken != lease.Token || stored.LeaseExpiresAt <= timeProvider.GetUtcNow())
            {
                return ValueTask.FromResult<JobLease?>(null);
            }

            var expiresAt = timeProvider.GetUtcNow().Add(leaseDuration);
            stored.LeaseExpiresAt = expiresAt;
            return ValueTask.FromResult<JobLease?>(new JobLease(stored.Job, lease.Token, expiresAt));
        }
    }

    public ValueTask<IReadOnlyList<Job>> GetDeadLettersAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            return ValueTask.FromResult<IReadOnlyList<Job>>(jobs.Values.Where(x => x.Job.Status == JobStatus.DeadLettered)
                .Select(x => x.Job).OrderBy(x => x.CompletedAt).ToArray());
        }
    }

    public ValueTask<bool> ReplayDeadLetterAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            if (!jobs.TryGetValue(jobId, out var stored) || stored.Job.Status != JobStatus.DeadLettered)
            {
                return ValueTask.FromResult(false);
            }

            stored.Job = stored.Job with { Status = JobStatus.Pending, ScheduledAt = timeProvider.GetUtcNow(), Attempt = 0, Failure = null, FailureKind = null, CompletedAt = null };
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<int> PurgeDeadLettersAsync(DateTimeOffset completedBefore, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            var ids = jobs.Where(x => x.Value.Job.Status == JobStatus.DeadLettered && x.Value.Job.CompletedAt < completedBefore.ToUniversalTime())
                .Select(x => x.Key).ToArray();
            foreach (var id in ids) jobs.Remove(id);
            return ValueTask.FromResult(ids.Length);
        }
    }

    public ValueTask<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            if (!jobs.TryGetValue(jobId, out var stored) || stored.Job.Status != JobStatus.Pending)
            {
                return ValueTask.FromResult(false);
            }

            stored.Job = stored.Job with { Status = JobStatus.Canceled };
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<Job?> GetAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            return ValueTask.FromResult(jobs.TryGetValue(jobId, out var stored) ? stored.Job : null);
        }
    }

    private ValueTask<bool> FinishAsync(
        JobLease lease,
        JobStatus status,
        JobFailure? failure,
        CancellationToken cancellationToken,
        DateTimeOffset? scheduledAt = null)
    {
        ArgumentNullException.ThrowIfNull(lease);
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            if (!jobs.TryGetValue(lease.Job.Id, out var stored) ||
                stored.Job.Status != JobStatus.Processing ||
                stored.LeaseToken != lease.Token)
            {
                return ValueTask.FromResult(false);
            }

            stored.Job = stored.Job with
            {
                Status = status,
                Failure = failure?.Message,
                FailureKind = failure?.Kind,
                ScheduledAt = scheduledAt ?? stored.Job.ScheduledAt,
                CompletedAt = status is JobStatus.Succeeded or JobStatus.Failed or JobStatus.Canceled or JobStatus.DeadLettered
                    ? timeProvider.GetUtcNow() : null,
            };
            stored.LeaseToken = null;
            stored.LeaseExpiresAt = null;
            return ValueTask.FromResult(true);
        }
    }

    private static bool IsClaimable(StoredJob stored, DateTimeOffset now) =>
        (stored.Job.Status == JobStatus.Pending && stored.Job.ScheduledAt <= now) ||
        (stored.Job.Status == JobStatus.Processing && stored.LeaseExpiresAt <= now);

    private sealed class StoredJob(Job job)
    {
        public Job Job { get; set; } = job;

        public Guid? LeaseToken { get; set; }

        public DateTimeOffset? LeaseExpiresAt { get; set; }
    }
}
