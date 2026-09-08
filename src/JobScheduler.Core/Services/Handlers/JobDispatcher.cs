using JobScheduler.Core.Jobs;
using JobScheduler.Core.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace JobScheduler.Core.Handlers;

internal sealed class JobDispatcher(IServiceProvider services, JobHandlerRegistry registry, IJobPayloadSerializer serializer) : IJobDispatcher
{
    public Task DispatchAsync(Job job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        return registry.TryGet(job.Type, out var registration)
            ? registration.Dispatch(this, registration.Upcast(job.Payload, job.PayloadVersion), cancellationToken)
            : throw new InvalidOperationException($"No handler is registered for job type '{job.Type}'.");
    }

    internal Task DispatchTypedAsync<TJob>(string payload, CancellationToken cancellationToken)
    {
        var message = serializer.Deserialize<TJob>(payload);
        return services.GetRequiredService<IJobHandler<TJob>>().HandleAsync(message, cancellationToken);
    }
}
