namespace JobScheduler.Core.Jobs;

public sealed record JobAttempt
{
    public required int Number { get; init; }

    public required string WorkerId { get; init; }

    public required DateTimeOffset ClaimedAt { get; init; }

    public DateTimeOffset LeaseExpiresAt { get; init; }

    public DateTimeOffset? LastRenewedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }

    public TimeSpan? Duration => FinishedAt - ClaimedAt;

    public JobStatus? Outcome { get; init; }

    public JobFailureKind? FailureKind { get; init; }

    public string? Failure { get; init; }

    public DateTimeOffset? RetryAt { get; init; }
}
