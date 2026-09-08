namespace JobScheduler.Core.Jobs;

public sealed record JobQuery
{
    public string? Queue { get; init; }

    public JobStatus? Status { get; init; }

    public string? Type { get; init; }

    public string? CorrelationId { get; init; }

    public DateTimeOffset? EnqueuedFrom { get; init; }

    public DateTimeOffset? EnqueuedThrough { get; init; }

    public string? Cursor { get; init; }

    public int Limit { get; init; } = 100;
}
