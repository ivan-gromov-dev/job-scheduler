namespace JobScheduler.Core.Jobs;

public enum JobStatus
{
    Pending,
    Processing,
    Succeeded,
    Failed,
    Canceled,
    DeadLettered,
}
