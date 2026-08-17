namespace JobScheduler.Worker;

public sealed class JobWorkerOptions
{
    public int MaxConcurrency { get; set; } = Environment.ProcessorCount;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(100);

    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);
}
