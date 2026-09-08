namespace JobScheduler.Core.Scheduling;

public sealed record ScheduleOptions
{
    public string? CronExpression { get; init; }
    public DateTimeOffset? RunAt { get; init; }
    public string TimeZoneId { get; init; } = TimeZoneInfo.Utc.Id;
    public MisfirePolicy MisfirePolicy { get; init; } = MisfirePolicy.Coalesce;

    public string Queue { get; init; } = "default";

    public int Priority { get; init; }

    public string? DeduplicationKey { get; init; }

    public string? CorrelationId { get; init; }

    public int PayloadVersion { get; init; } = 1;
}
