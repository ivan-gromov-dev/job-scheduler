using JobScheduler.Core.Handlers;
using JobScheduler.Core.Serialization;

namespace JobScheduler.Core.Jobs;

internal sealed class JobClient(IJobStore store, JobHandlerRegistry registry, IJobPayloadSerializer serializer) : IJobClient
{
    public ValueTask<Job> EnqueueAsync<TJob>(
        TJob job,
        DateTimeOffset? scheduledAt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        var type = registry.Get<TJob>();
        return store.EnqueueAsync(type.Name, serializer.Serialize(job), new JobEnqueueOptions { ScheduledAt = scheduledAt, PayloadVersion = type.Version }, cancellationToken);
    }

    public ValueTask<Job> EnqueueAsync<TJob>(
        TJob job,
        JobEnqueueOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(options);
        var type = registry.Get<TJob>();
        return store.EnqueueAsync(type.Name, serializer.Serialize(job), options with { PayloadVersion = type.Version }, cancellationToken);
    }

    public ValueTask<Job> ScheduleAsync<TJob>(
        TJob job,
        DateTimeOffset scheduledAt,
        CancellationToken cancellationToken = default) =>
        EnqueueAsync(job, scheduledAt, cancellationToken);
}
