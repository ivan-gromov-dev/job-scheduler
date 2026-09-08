using JobScheduler.Core.Scheduling;
using Microsoft.Extensions.Options;

namespace JobScheduler.Worker;

internal sealed class ScheduleMaterializer(IScheduleStore schedules, TimeProvider timeProvider, IOptions<ScheduleMaterializerOptions> options, JobWorkerState state, ScheduleMaterializerState materializerState) : BackgroundService
{
    public override Task StopAsync(CancellationToken cancellationToken)
    {
        state.BeginDrain();
        return base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        materializerState.Start();
        try
        {
            var settings = options.Value;
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(settings.PollInterval, TimeSpan.Zero);
            ArgumentOutOfRangeException.ThrowIfLessThan(settings.CatchUpLimit, 1);
            using var timer = new PeriodicTimer(settings.PollInterval, timeProvider);
            do
            {
                if (state.IsDraining) return;
                await schedules.MaterializeDueAsync(timeProvider.GetUtcNow(), settings.CatchUpLimit, stoppingToken);
                materializerState.Succeeded(timeProvider.GetUtcNow());
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception exception) { materializerState.Fail(exception); throw; }
        finally { materializerState.Stop(); }
    }
}
