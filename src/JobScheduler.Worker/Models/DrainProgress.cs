namespace JobScheduler.Worker;

public sealed record DrainProgress(bool IsDraining, int ActiveJobs, bool IsComplete);
