namespace JobScheduler.Core.Jobs;

public sealed record Job
{
    public required Guid Id { get; init; }

    public required string Type { get; init; }

    public required string Payload { get; init; }

    public DateTimeOffset EnqueuedAt { get; init; }

    public DateTimeOffset ScheduledAt { get; init; }

    public JobStatus Status { get; init; } = JobStatus.Pending;

    public int Attempt { get; init; }

    public string? Failure { get; init; }

    public static Job Create(
        string type,
        string payload,
        DateTimeOffset now,
        DateTimeOffset? scheduledAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(payload);

        return new Job
        {
            Id = Guid.NewGuid(),
            Type = type,
            Payload = payload,
            EnqueuedAt = now.ToUniversalTime(),
            ScheduledAt = (scheduledAt ?? now).ToUniversalTime(),
        };
    }
}
