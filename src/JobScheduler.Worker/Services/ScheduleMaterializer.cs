using JobScheduler.Core.Scheduling;
using Microsoft.Extensions.Options;

namespace JobScheduler.Worker;

internal sealed class ScheduleMaterializer(IScheduleStore schedules, TimeProvider timeProvider, IOptions<ScheduleMaterializerOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(settings.PollInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.CatchUpLimit, 1);
        using var timer = new PeriodicTimer(settings.PollInterval, timeProvider);
        do
        {
            await schedules.MaterializeDueAsync(timeProvider.GetUtcNow(), settings.CatchUpLimit, stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
