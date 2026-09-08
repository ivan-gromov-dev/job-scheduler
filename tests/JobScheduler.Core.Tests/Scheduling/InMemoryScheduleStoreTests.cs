using JobScheduler.Core.Jobs;
using JobScheduler.Core.Scheduling;

namespace JobScheduler.Core.Tests.Scheduling;

public sealed class InMemoryScheduleStoreTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OneOffScheduleMaterializesOnlyOnce()
    {
        var clock = new FixedTimeProvider(Start);
        var jobs = new InMemoryJobStore(clock);
        using var schedules = new InMemoryScheduleStore(jobs, new FixedTimeProvider(Start));
        var schedule = await schedules.CreateAsync("email", "{}", new ScheduleOptions { RunAt = Start.AddMinutes(5) });

        Assert.Equal(0, await schedules.MaterializeDueAsync(Start.AddMinutes(4)));
        Assert.Equal(1, await schedules.MaterializeDueAsync(Start.AddMinutes(5)));
        Assert.Equal(0, await schedules.MaterializeDueAsync(Start.AddHours(1)));
        Assert.Equal(schedule.Id, (await schedules.GetAsync(schedule.Id))!.Id);
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.NotNull(await jobs.ClaimAsync(TimeSpan.FromMinutes(1)));
    }

    [Theory]
    [InlineData(MisfirePolicy.Skip, 0)]
    [InlineData(MisfirePolicy.Coalesce, 1)]
    [InlineData(MisfirePolicy.CatchUp, 3)]
    public async Task AppliesMisfirePolicy(MisfirePolicy policy, int expected)
    {
        var jobs = new InMemoryJobStore(new FixedTimeProvider(Start));
        using var schedules = new InMemoryScheduleStore(jobs, new FixedTimeProvider(Start));
        await schedules.CreateAsync("report", "{}", new ScheduleOptions { CronExpression = "1 * * * *", MisfirePolicy = policy });

        Assert.Equal(expected, await schedules.MaterializeDueAsync(Start.AddHours(2).AddMinutes(1)));
    }

    [Fact]
    public async Task PauseResumeUpdateAndDeleteControlSchedule()
    {
        var jobs = new InMemoryJobStore(new FixedTimeProvider(Start));
        using var schedules = new InMemoryScheduleStore(jobs, new FixedTimeProvider(Start));
        var schedule = await schedules.CreateAsync("report", "{}", new ScheduleOptions { CronExpression = "0 11 * * *" });

        Assert.True(await schedules.PauseAsync(schedule.Id));
        Assert.Equal(0, await schedules.MaterializeDueAsync(Start.AddHours(2)));
        Assert.True(await schedules.ResumeAsync(schedule.Id));
        var updated = await schedules.UpdateAsync(schedule.Id, new ScheduleOptions { CronExpression = "30 10 * * *", MisfirePolicy = MisfirePolicy.CatchUp });
        Assert.Equal(Start.AddMinutes(30), updated!.NextOccurrence);
        Assert.True(await schedules.DeleteAsync(schedule.Id));
        Assert.Null(await schedules.GetAsync(schedule.Id));
    }

    [Fact]
    public async Task RejectsInvalidCronAndConflictingTriggers()
    {
        var jobs = new InMemoryJobStore(new FixedTimeProvider(Start));
        using var schedules = new InMemoryScheduleStore(jobs, new FixedTimeProvider(Start));
        await Assert.ThrowsAsync<FormatException>(() => schedules.CreateAsync("job", "{}", new ScheduleOptions { CronExpression = "bad" }).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => schedules.CreateAsync("job", "{}", new ScheduleOptions { CronExpression = "* * * * *", RunAt = Start }).AsTask());
    }

    [Fact]
    public void CronUsesUtcTimelineAcrossDaylightSavingTransitions()
    {
        var zone = FindBerlinTimeZone();
        var cron = CronSchedule.Parse("30 2 * * *");

        var spring = cron.GetNextOccurrence(new DateTimeOffset(2026, 3, 29, 0, 0, 0, TimeSpan.Zero), zone);
        Assert.Equal(new DateTimeOffset(2026, 3, 30, 0, 30, 0, TimeSpan.Zero), spring);

        var firstFall = cron.GetNextOccurrence(new DateTimeOffset(2026, 10, 24, 23, 0, 0, TimeSpan.Zero), zone);
        var secondFall = cron.GetNextOccurrence(firstFall, zone);
        Assert.Equal(TimeSpan.FromHours(1), secondFall - firstFall);
        Assert.Equal(TimeZoneInfo.ConvertTime(firstFall, zone).DateTime, TimeZoneInfo.ConvertTime(secondFall, zone).DateTime);
    }

    [Fact]
    public async Task CatchUpLimitRetainsRemainingBacklog()
    {
        var jobs = new InMemoryJobStore(new FixedTimeProvider(Start));
        using var schedules = new InMemoryScheduleStore(jobs, new FixedTimeProvider(Start));
        await schedules.CreateAsync("report", "{}", new ScheduleOptions { CronExpression = "1 * * * *", MisfirePolicy = MisfirePolicy.CatchUp });

        Assert.Equal(2, await schedules.MaterializeDueAsync(Start.AddHours(3).AddMinutes(1), 2));
        Assert.Equal(2, await schedules.MaterializeDueAsync(Start.AddHours(3).AddMinutes(1), 2));
        Assert.Equal(0, await schedules.MaterializeDueAsync(Start.AddHours(3).AddMinutes(1), 2));
    }

    [Fact]
    public async Task MaterializationCarriesDurableJobOptions()
    {
        var clock = new FixedTimeProvider(Start);
        var jobs = new InMemoryJobStore(clock);
        using var schedules = new InMemoryScheduleStore(jobs, clock);
        var options = new ScheduleOptions
        {
            RunAt = Start,
            Queue = "critical",
            Priority = 42,
            DeduplicationKey = "invoice-7",
            CorrelationId = "request-9",
            PayloadVersion = 3,
        };

        await schedules.CreateAsync("invoice", "{}", options);
        await schedules.MaterializeDueAsync(Start);
        var job = (await jobs.ClaimAsync(TimeSpan.FromMinutes(1)))!.Job;

        Assert.Equal("critical", job.Queue);
        Assert.Equal(42, job.Priority);
        Assert.Equal("invoice-7", job.DeduplicationKey);
        Assert.Equal("request-9", job.CorrelationId);
        Assert.Equal(3, job.PayloadVersion);
    }

    [Fact]
    public async Task AdministrationListsTriggersAndRecordsMaterializationHistory()
    {
        var clock = new FixedTimeProvider(Start);
        var jobs = new InMemoryJobStore(clock);
        using var schedules = new InMemoryScheduleStore(jobs, clock);
        var first = await schedules.CreateAsync("invoice", "{}", new ScheduleOptions { RunAt = Start.AddHours(1), Queue = "ops", CorrelationId = "request-1" });
        await schedules.CreateAsync("email", "{}", new ScheduleOptions { RunAt = Start.AddHours(2) });

        var page = await schedules.ListAsync(new ScheduleQuery { JobType = "invoice", Queue = "ops", CorrelationId = "request-1", NextThrough = Start.AddHours(1), Limit = 1 });
        var triggered = await schedules.TriggerAsync(first.Id);
        var inspected = await schedules.GetAsync(first.Id);

        Assert.Equal(first.Id, Assert.Single(page.Items).Id);
        Assert.Null(page.NextCursor);
        Assert.NotNull(triggered);
        Assert.Equal(triggered.Id, Assert.Single(inspected!.MaterializationHistory).JobId);
        Assert.Null(await schedules.TriggerAsync(Guid.NewGuid()));
    }

    private static TimeZoneInfo FindBerlinTimeZone()
    {
        foreach (var id in new[] { "Europe/Berlin", "W. Europe Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
        }
        throw new TimeZoneNotFoundException("Berlin time zone is unavailable.");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now = now.Add(duration);
    }
}
