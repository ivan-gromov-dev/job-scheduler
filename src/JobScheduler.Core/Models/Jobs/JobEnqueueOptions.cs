namespace JobScheduler.Core.Jobs;

public sealed record JobEnqueueOptions
{
    public DateTimeOffset? ScheduledAt { get; init; }

    public string? DeduplicationKey { get; init; }

    public string Queue { get; init; } = "default";

    public int Priority { get; init; }

    public string? CorrelationId { get; init; }

    public int? MaxQueueDepth { get; init; }
}
