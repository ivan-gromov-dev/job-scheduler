namespace JobScheduler.Worker;

public sealed class JobWorkerOptions
{
    public int MaxConcurrency { get; set; } = Environment.ProcessorCount;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(100);

    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan LeaseRenewalInterval { get; set; } = TimeSpan.FromMinutes(1);

    public TimeSpan ExecutionTimeout { get; set; } = TimeSpan.FromMinutes(30);

    public TimeSpan DeadLetterRetention { get; set; } = TimeSpan.FromDays(30);

    public RetryPolicy Retry { get; } = new();

    public Dictionary<string, int> QueueConcurrency { get; } = new(StringComparer.Ordinal);
}
