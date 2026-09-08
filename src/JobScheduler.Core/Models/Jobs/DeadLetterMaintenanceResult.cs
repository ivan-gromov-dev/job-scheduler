namespace JobScheduler.Core.Jobs;

public readonly record struct DeadLetterMaintenanceResult(bool IsOwner, int PurgedCount);
