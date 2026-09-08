namespace JobScheduler.Core.Scheduling;

public interface IScheduleClient
{
    ValueTask<Schedule> ScheduleOnceAsync<TJob>(TJob job, DateTimeOffset runAt, CancellationToken cancellationToken = default);
    ValueTask<Schedule> ScheduleRecurringAsync<TJob>(TJob job, string cronExpression, string timeZoneId, MisfirePolicy misfirePolicy = MisfirePolicy.Coalesce, CancellationToken cancellationToken = default);
}
