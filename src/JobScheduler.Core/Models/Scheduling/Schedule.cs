namespace JobScheduler.Core.Scheduling;

public sealed record Schedule
{
    public required Guid Id { get; init; }
    public required string JobType { get; init; }
    public required string Payload { get; init; }
    public int PayloadVersion { get; init; } = 1;
    public string? CronExpression { get; init; }
    public required string TimeZoneId { get; init; }
    public required MisfirePolicy MisfirePolicy { get; init; }
    public required DateTimeOffset NextOccurrence { get; init; }
    public bool IsPaused { get; init; }
    public long Version { get; init; }
    public string Queue { get; init; } = "default";
    public int Priority { get; init; }
    public string? DeduplicationKey { get; init; }
    public string? CorrelationId { get; init; }
    public IReadOnlyList<ScheduleMaterialization> MaterializationHistory { get; init; } = [];
}
