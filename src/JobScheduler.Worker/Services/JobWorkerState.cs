namespace JobScheduler.Worker;

public sealed class JobWorkerState
{
    private int running;
    private int draining;
    private int activeJobs;

    public bool IsRunning => Volatile.Read(ref running) == 1;
    public bool IsDraining => Volatile.Read(ref draining) == 1;
    public int ActiveJobs => Volatile.Read(ref activeJobs);

    internal void Start() => Volatile.Write(ref running, 1);
    internal void BeginDrain() => Volatile.Write(ref draining, 1);
    internal void Stop() => Volatile.Write(ref running, 0);
    internal void JobStarted() => Interlocked.Increment(ref activeJobs);
    internal void JobStopped() => Interlocked.Decrement(ref activeJobs);
}
