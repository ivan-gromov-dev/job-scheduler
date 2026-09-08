namespace JobScheduler.Core.Scheduling;

public interface IScheduleStore
{
    ValueTask<Schedule> CreateAsync(string jobType, string payload, ScheduleOptions options, CancellationToken cancellationToken = default);
    ValueTask<Schedule?> GetAsync(Guid scheduleId, CancellationToken cancellationToken = default);
    ValueTask<Schedule?> UpdateAsync(Guid scheduleId, ScheduleOptions options, CancellationToken cancellationToken = default);
    ValueTask<bool> PauseAsync(Guid scheduleId, CancellationToken cancellationToken = default);
    ValueTask<bool> ResumeAsync(Guid scheduleId, CancellationToken cancellationToken = default);
    ValueTask<bool> DeleteAsync(Guid scheduleId, CancellationToken cancellationToken = default);
    ValueTask<int> MaterializeDueAsync(DateTimeOffset through, int catchUpLimit = 100, CancellationToken cancellationToken = default);
}
