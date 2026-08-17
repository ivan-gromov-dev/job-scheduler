using JobScheduler.Core.Handlers;
using JobScheduler.Core.Jobs;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class JobSchedulerServiceCollectionExtensions
{
    public static IServiceCollection AddJobScheduler(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IJobStore, InMemoryJobStore>();
        services.TryAddSingleton(static provider =>
        {
            var registry = new JobHandlerRegistry();
            foreach (var registration in provider.GetServices<IJobHandlerRegistration>())
            {
                registration.Register(registry);
            }

            return registry;
        });
        services.TryAddSingleton<IJobClient, JobClient>();
        services.TryAddScoped<IJobDispatcher, JobDispatcher>();
        return services;
    }

    public static IServiceCollection AddJobHandler<TJob, THandler>(this IServiceCollection services)
        where THandler : class, IJobHandler<TJob>
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddJobScheduler();
        services.AddScoped<IJobHandler<TJob>, THandler>();
        services.AddSingleton<IJobHandlerRegistration>(new JobHandlerRegistration<TJob>());
        return services;
    }

    internal interface IJobHandlerRegistration
    {
        void Register(JobHandlerRegistry registry);
    }

    private sealed class JobHandlerRegistration<TJob> : IJobHandlerRegistration
    {
        public void Register(JobHandlerRegistry registry) => registry.Add<TJob>();
    }
}
