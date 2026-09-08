using JobScheduler.Core.Jobs;
using JobScheduler.Core.Scheduling;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JobScheduler.PostgreSql;

public sealed class PostgreSqlScheduleStore(NpgsqlDataSource dataSource, PostgreSqlJobStoreOptions options, PostgreSqlMigrator migrator, TimeProvider timeProvider) : IScheduleStore
{
    public async ValueTask<Schedule> CreateAsync(string jobType, string payload, ScheduleOptions options, CancellationToken cancellationToken = default)
    {
        Validate(jobType, payload, options);
        await EnsureMigratedAsync(cancellationToken);
        var entity = new ScheduleEntity
        {
            Id = Guid.NewGuid(),
            JobType = jobType,
            Payload = payload,
            CronExpression = options.CronExpression,
            TimeZoneId = options.TimeZoneId,
            MisfirePolicy = options.MisfirePolicy,
            NextOccurrence = First(options, timeProvider.GetUtcNow()),
        };
        await using var context = new JobSchedulerDbContext(dataSource);
        context.Schedules.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        return entity.ToSchedule();
    }

    public async ValueTask<Schedule?> GetAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        await EnsureMigratedAsync(cancellationToken);
        await using var context = new JobSchedulerDbContext(dataSource);
        return (await context.Schedules.AsNoTracking().SingleOrDefaultAsync(x => x.Id == scheduleId, cancellationToken))?.ToSchedule();
    }

    public async ValueTask<Schedule?> UpdateAsync(Guid scheduleId, ScheduleOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        await EnsureMigratedAsync(cancellationToken);
        await using var context = new JobSchedulerDbContext(dataSource);
        var entity = await context.Schedules.SingleOrDefaultAsync(x => x.Id == scheduleId, cancellationToken);
        if (entity is null) return null;
        Validate(entity.JobType, entity.Payload, options);
        entity.CronExpression = options.CronExpression; entity.TimeZoneId = options.TimeZoneId;
        entity.MisfirePolicy = options.MisfirePolicy; entity.NextOccurrence = First(options, timeProvider.GetUtcNow()); entity.Version++;
        await context.SaveChangesAsync(cancellationToken);
        return entity.ToSchedule();
    }

    public ValueTask<bool> PauseAsync(Guid scheduleId, CancellationToken cancellationToken = default) => SetPausedAsync(scheduleId, true, cancellationToken);
    public ValueTask<bool> ResumeAsync(Guid scheduleId, CancellationToken cancellationToken = default) => SetPausedAsync(scheduleId, false, cancellationToken);

    public async ValueTask<bool> DeleteAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        await EnsureMigratedAsync(cancellationToken);
        await using var context = new JobSchedulerDbContext(dataSource);
        return await context.Schedules.Where(x => x.Id == scheduleId).ExecuteDeleteAsync(cancellationToken) == 1;
    }

    public async ValueTask<int> MaterializeDueAsync(DateTimeOffset through, int catchUpLimit = 100, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(catchUpLimit, 1);
        await EnsureMigratedAsync(cancellationToken);
        through = through.ToUniversalTime();
        await using var context = new JobSchedulerDbContext(dataSource);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var due = await context.Schedules.FromSqlInterpolated($"SELECT * FROM job_scheduler_schedules WHERE is_paused = false AND next_occurrence <= {through} ORDER BY next_occurrence, id FOR UPDATE SKIP LOCKED").ToArrayAsync(cancellationToken);
        var count = 0;
        foreach (var schedule in due)
        {
            var occurrences = Due(schedule, through, catchUpLimit);
            foreach (var occurrence in occurrences)
            {
                context.Jobs.Add(JobEntity.FromJob(Job.Create(schedule.JobType, schedule.Payload, timeProvider.GetUtcNow(), occurrence, $"schedule:{schedule.Id:N}:{occurrence.UtcTicks}")));
                count++;
            }
            schedule.NextOccurrence = schedule.CronExpression is null ? DateTimeOffset.MaxValue :
                schedule.MisfirePolicy == MisfirePolicy.CatchUp && occurrences.Count == catchUpLimit ? Next(schedule, occurrences[^1]) : Next(schedule, through);
            schedule.Version++;
        }
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return count;
    }

    private async ValueTask<bool> SetPausedAsync(Guid id, bool paused, CancellationToken cancellationToken)
    {
        await EnsureMigratedAsync(cancellationToken);
        await using var context = new JobSchedulerDbContext(dataSource);
        return await context.Schedules.Where(x => x.Id == id && x.IsPaused != paused).ExecuteUpdateAsync(update => update.SetProperty(x => x.IsPaused, paused).SetProperty(x => x.Version, x => x.Version + 1), cancellationToken) == 1;
    }

    private async ValueTask EnsureMigratedAsync(CancellationToken cancellationToken)
    {
        if (options.AutoMigrate) await migrator.MigrateAsync(cancellationToken);
    }

    private static List<DateTimeOffset> Due(ScheduleEntity schedule, DateTimeOffset through, int limit)
    {
        if (schedule.CronExpression is null) return [schedule.NextOccurrence];
        if (schedule.MisfirePolicy == MisfirePolicy.Skip) return schedule.NextOccurrence == through ? [through] : [];
        if (schedule.MisfirePolicy == MisfirePolicy.Coalesce) return [through];
        var result = new List<DateTimeOffset>();
        var current = schedule.NextOccurrence;
        while (current <= through && result.Count < limit) { result.Add(current); current = Next(schedule, current); }
        return result;
    }

    private static DateTimeOffset Next(ScheduleEntity schedule, DateTimeOffset after) => CronSchedule.Parse(schedule.CronExpression!).GetNextOccurrence(after, TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId));
    private static DateTimeOffset First(ScheduleOptions schedule, DateTimeOffset now) => schedule.RunAt?.ToUniversalTime() ?? CronSchedule.Parse(schedule.CronExpression!).GetNextOccurrence(now, TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId));

    private static void Validate(string jobType, string payload, ScheduleOptions schedule)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobType); ArgumentNullException.ThrowIfNull(payload); ArgumentNullException.ThrowIfNull(schedule);
        if ((schedule.RunAt is null) == (schedule.CronExpression is null)) throw new ArgumentException("Specify exactly one of RunAt or CronExpression.", nameof(schedule));
        _ = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId);
        if (schedule.CronExpression is not null) _ = CronSchedule.Parse(schedule.CronExpression);
    }
}
