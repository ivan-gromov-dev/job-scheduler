namespace JobScheduler.Core.Jobs;

public sealed class JobQueueOptions
{
    public Dictionary<string, int> Capacities { get; } = new(StringComparer.Ordinal);

    public int? GetCapacity(string queue)
    {
        if (!Capacities.TryGetValue(queue, out var capacity)) return null;
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        return capacity;
    }
}
