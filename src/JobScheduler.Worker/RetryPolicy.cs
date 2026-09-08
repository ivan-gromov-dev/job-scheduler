namespace JobScheduler.Worker;

public sealed class RetryPolicy
{
    public int MaxAttempts { get; set; } = 5;

    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMinutes(5);

    public double BackoffMultiplier { get; set; } = 2;

    public double JitterFactor { get; set; } = 0.2;

    internal TimeSpan GetDelay(Guid jobId, int attempt)
    {
        var exponential = InitialDelay.TotalMilliseconds * Math.Pow(BackoffMultiplier, Math.Max(0, attempt - 1));
        var capped = Math.Min(exponential, MaxDelay.TotalMilliseconds);
        var hash = unchecked(((uint)jobId.GetHashCode() * 397u) + (uint)attempt);
        var unit = hash / (double)uint.MaxValue;
        var jitter = 1 + (((unit * 2) - 1) * JitterFactor);
        return TimeSpan.FromMilliseconds(Math.Max(0, capped * jitter));
    }
}
