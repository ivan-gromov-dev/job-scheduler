using System.Text.Json;
using JobScheduler.Core.Handlers;

namespace JobScheduler.Core.Jobs;

internal sealed class JobClient(IJobStore store) : IJobClient
{
    public ValueTask<Job> EnqueueAsync<TJob>(
        TJob job,
        DateTimeOffset? scheduledAt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        return store.EnqueueAsync(
            JobTypeName.For<TJob>(),
            JsonSerializer.Serialize(job),
            scheduledAt,
            cancellationToken);
    }

    public ValueTask<Job> EnqueueAsync<TJob>(
        TJob job,
        JobEnqueueOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(options);
        return store.EnqueueAsync(JobTypeName.For<TJob>(), JsonSerializer.Serialize(job), options, cancellationToken);
    }

    public ValueTask<Job> ScheduleAsync<TJob>(
        TJob job,
        DateTimeOffset scheduledAt,
        CancellationToken cancellationToken = default) =>
        EnqueueAsync(job, scheduledAt, cancellationToken);
}
