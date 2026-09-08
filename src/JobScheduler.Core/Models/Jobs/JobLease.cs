namespace JobScheduler.Core.Jobs;

public sealed record JobLease(Job Job, Guid Token, DateTimeOffset ExpiresAt, string WorkerId = "unknown");
