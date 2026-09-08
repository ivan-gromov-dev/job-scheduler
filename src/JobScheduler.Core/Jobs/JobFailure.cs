namespace JobScheduler.Core.Jobs;

public sealed record JobFailure(JobFailureKind Kind, string Message);
