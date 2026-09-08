using JobScheduler.Core.Jobs;
using System.Text.Json;

namespace JobScheduler.PostgreSql;

internal sealed class JobEntity
{
    public Guid Id { get; set; }
    public required string Type { get; set; }
    public required string Payload { get; set; }
    public int PayloadVersion { get; set; } = 1;
    public string AttemptHistory { get; set; } = "[]";
    public DateTimeOffset EnqueuedAt { get; set; }
    public DateTimeOffset ScheduledAt { get; set; }
    public JobStatus Status { get; set; }
    public int Attempt { get; set; }
    public string? Failure { get; set; }
    public JobFailureKind? FailureKind { get; set; }
    public string? DeduplicationKey { get; set; }
    public required string Queue { get; set; }
    public int Priority { get; set; }
    public string? CorrelationId { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public long RowVersion { get; set; }

    public Job ToJob() => new()
    {
        Id = Id,
        Type = Type,
        Payload = Payload,
        PayloadVersion = PayloadVersion,
        AttemptHistory = JsonSerializer.Deserialize<JobAttempt[]>(AttemptHistory) ?? [],
        EnqueuedAt = EnqueuedAt,
        ScheduledAt = ScheduledAt,
        Status = Status,
        Attempt = Attempt,
        Failure = Failure,
        FailureKind = FailureKind,
        DeduplicationKey = DeduplicationKey,
        Queue = Queue,
        Priority = Priority,
        CorrelationId = CorrelationId,
        CompletedAt = CompletedAt,
    };

    public static JobEntity FromJob(Job job) => new()
    {
        Id = job.Id,
        Type = job.Type,
        Payload = job.Payload,
        PayloadVersion = job.PayloadVersion,
        AttemptHistory = JsonSerializer.Serialize(job.AttemptHistory),
        EnqueuedAt = job.EnqueuedAt,
        ScheduledAt = job.ScheduledAt,
        Status = job.Status,
        Attempt = job.Attempt,
        Failure = job.Failure,
        FailureKind = job.FailureKind,
        DeduplicationKey = job.DeduplicationKey,
        Queue = job.Queue,
        Priority = job.Priority,
        CorrelationId = job.CorrelationId,
        CompletedAt = job.CompletedAt,
    };
}
