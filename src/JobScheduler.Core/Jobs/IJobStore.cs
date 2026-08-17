namespace JobScheduler.Core.Jobs;

public interface IJobStore
{
    ValueTask<Job> EnqueueAsync(
        string type,
        string payload,
        DateTimeOffset? scheduledAt = null,
        CancellationToken cancellationToken = default);

    ValueTask<JobLease?> ClaimAsync(
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    ValueTask<bool> CompleteAsync(JobLease lease, CancellationToken cancellationToken = default);

    ValueTask<bool> FailAsync(
        JobLease lease,
        string failure,
        CancellationToken cancellationToken = default);

    ValueTask<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken = default);

    ValueTask<Job?> GetAsync(Guid jobId, CancellationToken cancellationToken = default);
}
