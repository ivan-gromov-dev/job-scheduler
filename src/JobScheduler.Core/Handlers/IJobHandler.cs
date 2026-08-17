namespace JobScheduler.Core.Handlers;

public interface IJobHandler<in TJob>
{
    Task HandleAsync(TJob job, CancellationToken cancellationToken);
}
