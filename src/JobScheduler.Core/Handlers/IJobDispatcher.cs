using JobScheduler.Core.Jobs;

namespace JobScheduler.Core.Handlers;

public interface IJobDispatcher
{
    Task DispatchAsync(Job job, CancellationToken cancellationToken);
}
