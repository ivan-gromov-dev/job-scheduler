namespace JobScheduler.Core.Jobs;

public sealed record JobQuery
{
    public string? Queue { get; init; }

    public JobStatus? Status { get; init; }

    public int Limit { get; init; } = 100;
}
