using System.Text.Json;
using JobScheduler.Core.Handlers;

namespace JobScheduler.Core.Scheduling;

internal sealed class ScheduleClient(IScheduleStore store) : IScheduleClient
{
    public ValueTask<Schedule> ScheduleOnceAsync<TJob>(TJob job, DateTimeOffset runAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        return store.CreateAsync(JobTypeName.For<TJob>(), JsonSerializer.Serialize(job), new ScheduleOptions { RunAt = runAt }, cancellationToken);
    }

    public ValueTask<Schedule> ScheduleRecurringAsync<TJob>(TJob job, string cronExpression, string timeZoneId, MisfirePolicy misfirePolicy = MisfirePolicy.Coalesce, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        return store.CreateAsync(JobTypeName.For<TJob>(), JsonSerializer.Serialize(job), new ScheduleOptions { CronExpression = cronExpression, TimeZoneId = timeZoneId, MisfirePolicy = misfirePolicy }, cancellationToken);
    }
}
