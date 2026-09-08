using JobScheduler.Core.Jobs;

namespace JobScheduler.Core.Scheduling;

public sealed class InMemoryScheduleStore(IJobStore jobs, TimeProvider timeProvider) : IScheduleStore, IDisposable
{
    private readonly SemaphoreSlim sync = new(1, 1);
    private readonly Dictionary<Guid, Schedule> schedules = [];

    public async ValueTask<Schedule> CreateAsync(string jobType, string payload, ScheduleOptions options, CancellationToken cancellationToken = default)
    {
        Validate(jobType, payload, options);
        var next = GetFirstOccurrence(options, timeProvider.GetUtcNow());
        var schedule = new Schedule { Id = Guid.NewGuid(), JobType = jobType, Payload = payload, PayloadVersion = options.PayloadVersion, CronExpression = options.CronExpression, TimeZoneId = options.TimeZoneId, MisfirePolicy = options.MisfirePolicy, NextOccurrence = next, Queue = options.Queue, Priority = options.Priority, DeduplicationKey = options.DeduplicationKey, CorrelationId = options.CorrelationId };
        await sync.WaitAsync(cancellationToken);
        try { schedules.Add(schedule.Id, schedule); }
        finally { sync.Release(); }
        return schedule;
    }

    public async ValueTask<Schedule?> GetAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        await sync.WaitAsync(cancellationToken);
        try { return schedules.GetValueOrDefault(scheduleId); }
        finally { sync.Release(); }
    }

    public async ValueTask<Schedule?> UpdateAsync(Guid scheduleId, ScheduleOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        await sync.WaitAsync(cancellationToken);
        try
        {
            if (!schedules.TryGetValue(scheduleId, out var current)) return null;
            Validate(current.JobType, current.Payload, options);
            var updated = current with { CronExpression = options.CronExpression, TimeZoneId = options.TimeZoneId, MisfirePolicy = options.MisfirePolicy, NextOccurrence = GetFirstOccurrence(options, timeProvider.GetUtcNow()), Queue = options.Queue, Priority = options.Priority, DeduplicationKey = options.DeduplicationKey, CorrelationId = options.CorrelationId, Version = current.Version + 1 };
            schedules[scheduleId] = updated;
            return updated;
        }
        finally { sync.Release(); }
    }

    public ValueTask<bool> PauseAsync(Guid scheduleId, CancellationToken cancellationToken = default) => SetPausedAsync(scheduleId, true, cancellationToken);
    public ValueTask<bool> ResumeAsync(Guid scheduleId, CancellationToken cancellationToken = default) => SetPausedAsync(scheduleId, false, cancellationToken);

    public async ValueTask<bool> DeleteAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        await sync.WaitAsync(cancellationToken);
        try { return schedules.Remove(scheduleId); }
        finally { sync.Release(); }
    }

    public async ValueTask<int> MaterializeDueAsync(DateTimeOffset through, int catchUpLimit = 100, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(catchUpLimit, 1);
        through = through.ToUniversalTime();
        await sync.WaitAsync(cancellationToken);
        try
        {
            var count = 0;
            foreach (var schedule in schedules.Values.Where(x => !x.IsPaused && x.NextOccurrence <= through).ToArray())
            {
                var updatedSchedule = schedule;
                var occurrences = GetDueOccurrences(schedule, through, catchUpLimit);
                foreach (var occurrence in occurrences)
                {
                    var job = await jobs.EnqueueAsync(schedule.JobType, schedule.Payload, new JobEnqueueOptions { ScheduledAt = occurrence, DeduplicationKey = schedule.DeduplicationKey ?? $"schedule:{schedule.Id:N}:{occurrence.UtcTicks}", Queue = schedule.Queue, Priority = schedule.Priority, CorrelationId = schedule.CorrelationId, PayloadVersion = schedule.PayloadVersion }, cancellationToken);
                    updatedSchedule = updatedSchedule with { MaterializationHistory = [.. updatedSchedule.MaterializationHistory, new ScheduleMaterialization(occurrence, job.Id, timeProvider.GetUtcNow())] };
                    count++;
                }
                var next = schedule.CronExpression is null ? DateTimeOffset.MaxValue :
                    schedule.MisfirePolicy == MisfirePolicy.CatchUp && occurrences.Count == catchUpLimit ? Next(schedule, occurrences[^1]) : Next(schedule, through);
                schedules[schedule.Id] = updatedSchedule with { NextOccurrence = next, Version = schedule.Version + 1 };
            }
            return count;
        }
        finally { sync.Release(); }
    }

    public async ValueTask<SchedulePage> ListAsync(ScheduleQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Limit, 1);
        await sync.WaitAsync(cancellationToken);
        try
        {
            (DateTimeOffset Next, Guid Id)? cursor = query.Cursor is null ? null : DecodeCursor(query.Cursor);
            var items = schedules.Values
                .Where(x => query.JobType is null || x.JobType == query.JobType)
                .Where(x => query.Queue is null || x.Queue == query.Queue)
                .Where(x => query.CorrelationId is null || x.CorrelationId == query.CorrelationId)
                .Where(x => query.IsPaused is null || x.IsPaused == query.IsPaused)
                .Where(x => query.NextFrom is null || x.NextOccurrence >= query.NextFrom.Value.ToUniversalTime())
                .Where(x => query.NextThrough is null || x.NextOccurrence <= query.NextThrough.Value.ToUniversalTime())
                .OrderBy(x => x.NextOccurrence).ThenBy(x => x.Id)
                .Where(x => cursor is null || x.NextOccurrence > cursor.Value.Next || (x.NextOccurrence == cursor.Value.Next && x.Id.CompareTo(cursor.Value.Id) > 0))
                .Take(checked(query.Limit + 1)).ToArray();
            var page = items.Take(query.Limit).ToArray();
            var next = items.Length > query.Limit ? EncodeCursor(page[^1]) : null;
            return new SchedulePage(page, next);
        }
        finally { sync.Release(); }
    }

    public async ValueTask<Job?> TriggerAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        await sync.WaitAsync(cancellationToken);
        try
        {
            if (!schedules.TryGetValue(scheduleId, out var schedule)) return null;
            var now = timeProvider.GetUtcNow();
            var job = await jobs.EnqueueAsync(schedule.JobType, schedule.Payload, new JobEnqueueOptions { ScheduledAt = now, Queue = schedule.Queue, Priority = schedule.Priority, CorrelationId = schedule.CorrelationId, PayloadVersion = schedule.PayloadVersion }, cancellationToken);
            schedules[scheduleId] = schedule with { MaterializationHistory = [.. schedule.MaterializationHistory, new ScheduleMaterialization(now, job.Id, now)], Version = schedule.Version + 1 };
            return job;
        }
        finally { sync.Release(); }
    }

    private static string EncodeCursor(Schedule schedule) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{schedule.NextOccurrence.UtcTicks}:{schedule.Id:N}"));
    private static (DateTimeOffset Next, Guid Id) DecodeCursor(string value)
    {
        try { var parts = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value)).Split(':', 2); return (new DateTimeOffset(long.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), TimeSpan.Zero), Guid.ParseExact(parts[1], "N")); }
        catch (Exception exception) when (exception is FormatException or ArgumentException or OverflowException) { throw new ArgumentException("The cursor is invalid.", nameof(value), exception); }
    }

    private async ValueTask<bool> SetPausedAsync(Guid id, bool paused, CancellationToken cancellationToken)
    {
        await sync.WaitAsync(cancellationToken);
        try
        {
            if (!schedules.TryGetValue(id, out var schedule) || schedule.IsPaused == paused) return false;
            schedules[id] = schedule with { IsPaused = paused, Version = schedule.Version + 1 };
            return true;
        }
        finally { sync.Release(); }
    }

    private static List<DateTimeOffset> GetDueOccurrences(Schedule schedule, DateTimeOffset through, int limit)
    {
        if (schedule.CronExpression is null || schedule.MisfirePolicy == MisfirePolicy.Coalesce) return [schedule.MisfirePolicy == MisfirePolicy.Coalesce ? through : schedule.NextOccurrence];
        if (schedule.MisfirePolicy == MisfirePolicy.Skip) return schedule.NextOccurrence == through ? [through] : [];
        var result = new List<DateTimeOffset>();
        var occurrence = schedule.NextOccurrence;
        while (occurrence <= through && result.Count < limit) { result.Add(occurrence); occurrence = Next(schedule, occurrence); }
        return result;
    }

    private static DateTimeOffset Next(Schedule schedule, DateTimeOffset after) => CronSchedule.Parse(schedule.CronExpression!).GetNextOccurrence(after, TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId));
    private static DateTimeOffset GetFirstOccurrence(ScheduleOptions options, DateTimeOffset now) => options.RunAt?.ToUniversalTime() ?? CronSchedule.Parse(options.CronExpression!).GetNextOccurrence(now, TimeZoneInfo.FindSystemTimeZoneById(options.TimeZoneId));

    private static void Validate(string jobType, string payload, ScheduleOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobType); ArgumentNullException.ThrowIfNull(payload); ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Queue);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.PayloadVersion, 1);
        if ((options.RunAt is null) == (options.CronExpression is null)) throw new ArgumentException("Specify exactly one of RunAt or CronExpression.", nameof(options));
        _ = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZoneId);
        if (options.CronExpression is not null) _ = CronSchedule.Parse(options.CronExpression);
    }

    public void Dispose() => sync.Dispose();
}
