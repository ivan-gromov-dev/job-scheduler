using JobScheduler.Core.Jobs;

namespace JobScheduler.PostgreSql;

internal sealed class JobEntity
{
    public Guid Id { get; set; }
    public required string Type { get; set; }
    public required string Payload { get; set; }
    public DateTimeOffset EnqueuedAt { get; set; }
    public DateTimeOffset ScheduledAt { get; set; }
    public JobStatus Status { get; set; }
    public int Attempt { get; set; }
    public string? Failure { get; set; }
    public JobFailureKind? FailureKind { get; set; }
    public string? DeduplicationKey { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public long RowVersion { get; set; }

    public Job ToJob() => new()
    {
        Id = Id,
        Type = Type,
        Payload = Payload,
        EnqueuedAt = EnqueuedAt,
        ScheduledAt = ScheduledAt,
        Status = Status,
        Attempt = Attempt,
        Failure = Failure,
        FailureKind = FailureKind,
        DeduplicationKey = DeduplicationKey,
        CompletedAt = CompletedAt,
    };

    public static JobEntity FromJob(Job job) => new()
    {
        Id = job.Id,
        Type = job.Type,
        Payload = job.Payload,
        EnqueuedAt = job.EnqueuedAt,
        ScheduledAt = job.ScheduledAt,
        Status = job.Status,
        Attempt = job.Attempt,
        Failure = job.Failure,
        FailureKind = job.FailureKind,
        DeduplicationKey = job.DeduplicationKey,
        CompletedAt = job.CompletedAt,
    };
}
