namespace JobScheduler.Worker;

public sealed class JobWorkerOptions
{
    public int MaxConcurrency { get; set; } = Environment.ProcessorCount;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(100);

    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan LeaseRenewalInterval { get; set; } = TimeSpan.FromMinutes(1);

    public TimeSpan ExecutionTimeout { get; set; } = TimeSpan.FromMinutes(30);

    public TimeSpan DeadLetterRetention { get; set; } = TimeSpan.FromDays(30);

    public TimeSpan MaintenanceInterval { get; set; } = TimeSpan.FromMinutes(5);

    public int MaintenanceBatchSize { get; set; } = 100;

    public TimeSpan DrainTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public bool CancelHandlersAfterDrainTimeout { get; set; } = true;

    public RetryPolicy Retry { get; } = new();

    public Dictionary<string, int> QueueConcurrency { get; } = new(StringComparer.Ordinal);
}
