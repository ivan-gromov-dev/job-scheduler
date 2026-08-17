namespace JobScheduler.Core.Jobs;

public sealed class InMemoryJobStore(TimeProvider timeProvider) : IJobStore
{
    private readonly Lock sync = new();
    private readonly Dictionary<Guid, StoredJob> jobs = [];

    public ValueTask<Job> EnqueueAsync(
        string type,
        string payload,
        DateTimeOffset? scheduledAt = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = timeProvider.GetUtcNow();
        var job = Job.Create(type, payload, now, scheduledAt);
        lock (sync)
        {
            jobs.Add(job.Id, new StoredJob(job));
        }

        return ValueTask.FromResult(job);
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
        return FinishAsync(lease, JobStatus.Failed, failure, cancellationToken);
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
        string? failure,
        CancellationToken cancellationToken)
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

            stored.Job = stored.Job with { Status = status, Failure = failure };
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
