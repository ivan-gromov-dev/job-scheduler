using JobScheduler.Core.Jobs;

namespace JobScheduler.Core.Scheduling;

public sealed record ScheduleQuery
{
    public string? JobType { get; init; }
    public string? Queue { get; init; }
    public string? CorrelationId { get; init; }
    public bool? IsPaused { get; init; }
    public DateTimeOffset? NextFrom { get; init; }
    public DateTimeOffset? NextThrough { get; init; }
    public string? Cursor { get; init; }
    public int Limit { get; init; } = 100;
}

public sealed record SchedulePage(IReadOnlyList<Schedule> Items, string? NextCursor);
