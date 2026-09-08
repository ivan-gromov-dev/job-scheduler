namespace JobScheduler.Core.Scheduling;

public sealed record ScheduleMaterialization(DateTimeOffset Occurrence, Guid JobId, DateTimeOffset MaterializedAt);
