using JobScheduler.Core.Handlers;
using JobScheduler.Core.Serialization;

namespace JobScheduler.Core.Scheduling;

internal sealed class ScheduleClient(IScheduleStore store, JobHandlerRegistry registry, IJobPayloadSerializer serializer) : IScheduleClient
{
    public ValueTask<Schedule> ScheduleOnceAsync<TJob>(TJob job, DateTimeOffset runAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        return ScheduleAsync(job, new ScheduleOptions { RunAt = runAt }, cancellationToken);
    }

    public ValueTask<Schedule> ScheduleRecurringAsync<TJob>(TJob job, string cronExpression, string timeZoneId, MisfirePolicy misfirePolicy = MisfirePolicy.Coalesce, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        return ScheduleAsync(job, new ScheduleOptions { CronExpression = cronExpression, TimeZoneId = timeZoneId, MisfirePolicy = misfirePolicy }, cancellationToken);
    }

    public ValueTask<Schedule> ScheduleAsync<TJob>(TJob job, ScheduleOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(options);
        var type = registry.Get<TJob>();
        return store.CreateAsync(type.Name, serializer.Serialize(job), options with { PayloadVersion = type.Version }, cancellationToken);
    }
}
