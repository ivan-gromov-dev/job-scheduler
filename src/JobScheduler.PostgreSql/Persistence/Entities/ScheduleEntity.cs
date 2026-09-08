using JobScheduler.Core.Scheduling;

namespace JobScheduler.PostgreSql;

internal sealed class ScheduleEntity
{
    public Guid Id { get; set; }
    public required string JobType { get; set; }
    public required string Payload { get; set; }
    public int PayloadVersion { get; set; } = 1;
    public string? CronExpression { get; set; }
    public required string TimeZoneId { get; set; }
    public MisfirePolicy MisfirePolicy { get; set; }
    public DateTimeOffset NextOccurrence { get; set; }
    public bool IsPaused { get; set; }
    public long Version { get; set; }
    public required string Queue { get; set; } = "default";
    public int Priority { get; set; }
    public string? DeduplicationKey { get; set; }
    public string? CorrelationId { get; set; }
    public string MaterializationHistory { get; set; } = "[]";

    public Schedule ToSchedule() => new() { Id = Id, JobType = JobType, Payload = Payload, PayloadVersion = PayloadVersion, CronExpression = CronExpression, TimeZoneId = TimeZoneId, MisfirePolicy = MisfirePolicy, NextOccurrence = NextOccurrence, IsPaused = IsPaused, Version = Version, Queue = Queue, Priority = Priority, DeduplicationKey = DeduplicationKey, CorrelationId = CorrelationId, MaterializationHistory = System.Text.Json.JsonSerializer.Deserialize<ScheduleMaterialization[]>(MaterializationHistory) ?? [] };

    public void Record(ScheduleMaterialization materialization)
    {
        var history = System.Text.Json.JsonSerializer.Deserialize<List<ScheduleMaterialization>>(MaterializationHistory) ?? [];
        history.Add(materialization);
        MaterializationHistory = System.Text.Json.JsonSerializer.Serialize(history);
    }
}
