namespace JobScheduler.Core.Jobs;

public sealed record JobEnqueueOptions
{
    public DateTimeOffset? ScheduledAt { get; init; }

    public string? DeduplicationKey { get; init; }
}
