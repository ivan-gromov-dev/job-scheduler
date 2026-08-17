namespace JobScheduler.Core.Jobs;

public interface IJobClient
{
    ValueTask<Job> EnqueueAsync<TJob>(
        TJob job,
        DateTimeOffset? scheduledAt = null,
        CancellationToken cancellationToken = default);

    ValueTask<Job> ScheduleAsync<TJob>(
        TJob job,
        DateTimeOffset scheduledAt,
        CancellationToken cancellationToken = default);
}
