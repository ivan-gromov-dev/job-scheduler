namespace JobScheduler.Core.Jobs;

public enum JobFailureKind
{
    Transient,
    Permanent,
    Timeout,
    Cancellation,
}
