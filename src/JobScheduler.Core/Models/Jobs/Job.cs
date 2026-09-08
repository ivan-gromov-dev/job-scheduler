namespace JobScheduler.Core.Jobs;

public sealed record Job
{
    public required Guid Id { get; init; }

    public required string Type { get; init; }

    public required string Payload { get; init; }

    public int PayloadVersion { get; init; } = 1;

    public DateTimeOffset EnqueuedAt { get; init; }

    public DateTimeOffset ScheduledAt { get; init; }

    public JobStatus Status { get; init; } = JobStatus.Pending;

    public int Attempt { get; init; }

    public string? Failure { get; init; }

    public JobFailureKind? FailureKind { get; init; }

    public string? DeduplicationKey { get; init; }

    public string Queue { get; init; } = "default";

    public int Priority { get; init; }

    public string? CorrelationId { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public IReadOnlyList<JobAttempt> AttemptHistory { get; init; } = [];

    public static Job Create(
        string type,
        string payload,
        DateTimeOffset now,
        DateTimeOffset? scheduledAt = null,
        string? deduplicationKey = null,
        string queue = "default",
        int priority = 0,
        string? correlationId = null,
        int payloadVersion = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(queue);
        ArgumentOutOfRangeException.ThrowIfLessThan(payloadVersion, 1);

        return new Job
        {
            Id = Guid.NewGuid(),
            Type = type,
            Payload = payload,
            EnqueuedAt = now.ToUniversalTime(),
            ScheduledAt = (scheduledAt ?? now).ToUniversalTime(),
            DeduplicationKey = deduplicationKey,
            Queue = queue,
            Priority = priority,
            CorrelationId = correlationId,
            PayloadVersion = payloadVersion,
        };
    }
}
