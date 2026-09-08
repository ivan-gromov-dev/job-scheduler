namespace JobScheduler.Worker;

public sealed class JobWorkerState : IJobWorkerControl
{
    private int running;
    private int draining;
    private int activeJobs;

    public bool IsRunning => Volatile.Read(ref running) == 1;
    public bool IsDraining => Volatile.Read(ref draining) == 1;
    public int ActiveJobs => Volatile.Read(ref activeJobs);

    internal void Start() => Volatile.Write(ref running, 1);
    internal void Stop() => Volatile.Write(ref running, 0);
    internal void JobStarted() => Interlocked.Increment(ref activeJobs);
    internal void JobStopped() => Interlocked.Decrement(ref activeJobs);

    public DrainProgress GetProgress() => new(IsDraining, ActiveJobs, IsDraining && ActiveJobs == 0);

    public DrainProgress BeginDrain()
    {
        Volatile.Write(ref draining, 1);
        return GetProgress();
    }
}
