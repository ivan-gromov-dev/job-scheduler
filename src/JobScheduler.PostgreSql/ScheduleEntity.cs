using JobScheduler.Core.Scheduling;

namespace JobScheduler.PostgreSql;

internal sealed class ScheduleEntity
{
    public Guid Id { get; set; }
    public required string JobType { get; set; }
    public required string Payload { get; set; }
    public string? CronExpression { get; set; }
    public required string TimeZoneId { get; set; }
    public MisfirePolicy MisfirePolicy { get; set; }
    public DateTimeOffset NextOccurrence { get; set; }
    public bool IsPaused { get; set; }
    public long Version { get; set; }

    public Schedule ToSchedule() => new() { Id = Id, JobType = JobType, Payload = Payload, CronExpression = CronExpression, TimeZoneId = TimeZoneId, MisfirePolicy = MisfirePolicy, NextOccurrence = NextOccurrence, IsPaused = IsPaused, Version = Version };
}
