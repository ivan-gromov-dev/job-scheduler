namespace JobScheduler.Core.Jobs;

public interface IJobStore
{
    ValueTask<Job> EnqueueAsync(
        string type,
        string payload,
        DateTimeOffset? scheduledAt = null,
        CancellationToken cancellationToken = default);

    ValueTask<Job> EnqueueAsync(
        string type,
        string payload,
        JobEnqueueOptions options,
        CancellationToken cancellationToken = default);

    ValueTask<JobLease?> ClaimAsync(
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    ValueTask<JobLease?> ClaimAsync(
        TimeSpan leaseDuration,
        IReadOnlyCollection<string> queues,
        CancellationToken cancellationToken = default);

    ValueTask<JobLease?> ClaimAsync(
        TimeSpan leaseDuration,
        IReadOnlyCollection<string> queues,
        string workerId,
        CancellationToken cancellationToken = default) =>
        ClaimAsync(leaseDuration, queues, cancellationToken);

    ValueTask<bool> CompleteAsync(JobLease lease, CancellationToken cancellationToken = default);

    ValueTask<bool> FailAsync(
        JobLease lease,
        string failure,
        CancellationToken cancellationToken = default);

    ValueTask<bool> RetryAsync(
        JobLease lease,
        JobFailure failure,
        DateTimeOffset retryAt,
        CancellationToken cancellationToken = default);

    ValueTask<bool> DeadLetterAsync(
        JobLease lease,
        JobFailure failure,
        CancellationToken cancellationToken = default);

    ValueTask<JobLease?> RenewLeaseAsync(
        JobLease lease,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<Job>> GetDeadLettersAsync(CancellationToken cancellationToken = default);

    ValueTask<bool> ReplayDeadLetterAsync(Guid jobId, CancellationToken cancellationToken = default);

    ValueTask<int> PurgeDeadLettersAsync(DateTimeOffset completedBefore, CancellationToken cancellationToken = default);

    async ValueTask<DeadLetterMaintenanceResult> PurgeDeadLettersBatchAsync(
        DateTimeOffset completedBefore,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        return new DeadLetterMaintenanceResult(
            true,
            await PurgeDeadLettersAsync(completedBefore, cancellationToken));
    }

    ValueTask<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken = default);

    ValueTask<Job?> GetAsync(Guid jobId, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<Job>> ListAsync(
        JobQuery query,
        CancellationToken cancellationToken = default);
}
