namespace JobScheduler.Worker;

public sealed class ScheduleMaterializerOptions
{
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(15);
    public int CatchUpLimit { get; set; } = 100;
}
