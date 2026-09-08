namespace JobScheduler.Core.Jobs;

public sealed class JobExecutionException : Exception
{
    public JobExecutionException(JobFailureKind kind, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public JobFailureKind Kind { get; }
}
