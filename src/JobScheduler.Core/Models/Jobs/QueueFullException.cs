namespace JobScheduler.Core.Jobs;

public sealed class QueueFullException(string queue, int capacity)
    : InvalidOperationException($"Queue '{queue}' has reached its capacity of {capacity} jobs.")
{
    public string Queue { get; } = queue;

    public int Capacity { get; } = capacity;
}
