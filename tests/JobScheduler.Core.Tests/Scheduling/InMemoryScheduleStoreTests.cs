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
