using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace JobScheduler.Worker;

public static class JobSchedulerTelemetry
{
    public const string SourceName = "JobScheduler";

    internal static readonly ActivitySource Activities = new(SourceName);
    internal static readonly Meter Meter = new(SourceName);
    internal static readonly Counter<long> Claimed = Meter.CreateCounter<long>("job_scheduler.jobs.claimed");
    internal static readonly Counter<long> Completed = Meter.CreateCounter<long>("job_scheduler.jobs.completed");
    internal static readonly Counter<long> Retried = Meter.CreateCounter<long>("job_scheduler.jobs.retried");
    internal static readonly Counter<long> DeadLettered = Meter.CreateCounter<long>("job_scheduler.jobs.dead_lettered");
    internal static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("job_scheduler.job.duration", "s");
    internal static readonly Histogram<double> Lag = Meter.CreateHistogram<double>("job_scheduler.queue.lag", "s");
}
