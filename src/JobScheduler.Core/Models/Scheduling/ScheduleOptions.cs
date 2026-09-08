namespace JobScheduler.Core.Scheduling;

public sealed record ScheduleOptions
{
    public string? CronExpression { get; init; }
    public DateTimeOffset? RunAt { get; init; }
    public string TimeZoneId { get; init; } = TimeZoneInfo.Utc.Id;
    public MisfirePolicy MisfirePolicy { get; init; } = MisfirePolicy.Coalesce;
}
