namespace JobScheduler.Worker;

public class Worker(ILogger<Worker> logger) : BackgroundService
{
    private static readonly Action<ILogger, DateTimeOffset, Exception?> LogWorkerHeartbeat =
        LoggerMessage.Define<DateTimeOffset>(
            LogLevel.Information,
            new EventId(1, nameof(LogWorkerHeartbeat)),
            "Worker running at: {Timestamp}");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            LogWorkerHeartbeat(logger, DateTimeOffset.UtcNow, null);
            await Task.Delay(1000, stoppingToken);
        }
    }
}
