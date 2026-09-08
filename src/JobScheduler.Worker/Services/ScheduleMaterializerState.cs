namespace JobScheduler.Worker;

public sealed class ScheduleMaterializerState
{
    private int running;
    public bool IsRunning => Volatile.Read(ref running) == 1;
    public DateTimeOffset? LastSuccessfulRun { get; private set; }
    public Exception? FatalError { get; private set; }
    internal void Start() => Volatile.Write(ref running, 1);
    internal void Succeeded(DateTimeOffset at) => LastSuccessfulRun = at;
    internal void Fail(Exception exception) { FatalError = exception; Volatile.Write(ref running, 0); }
    internal void Stop() => Volatile.Write(ref running, 0);
}
