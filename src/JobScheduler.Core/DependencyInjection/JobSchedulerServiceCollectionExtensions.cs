using JobScheduler.Core.Handlers;
using JobScheduler.Core.Jobs;
using JobScheduler.Core.Scheduling;
using JobScheduler.Core.Serialization;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class JobSchedulerServiceCollectionExtensions
{
    public static IServiceCollection AddJobScheduler(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<JobQueueOptions>();
        services.TryAddSingleton<IJobPayloadSerializer, JsonJobPayloadSerializer>();
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
        services.TryAddSingleton<IJobAdministration, JobAdministration>();
        services.TryAddSingleton<IScheduleStore, InMemoryScheduleStore>();
        services.TryAddSingleton<IScheduleClient, ScheduleClient>();
        services.TryAddScoped<IJobDispatcher, JobDispatcher>();
        return services;
    }

    public static IServiceCollection AddJobScheduler(
        this IServiceCollection services,
        Action<JobQueueOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new JobQueueOptions();
        configure(options);
        services.Replace(ServiceDescriptor.Singleton(options));
        return services.AddJobScheduler();
    }

    public static IServiceCollection AddJobHandler<TJob, THandler>(this IServiceCollection services)
        where THandler : class, IJobHandler<TJob>
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddJobScheduler();
        services.AddScoped<IJobHandler<TJob>, THandler>();
        services.AddSingleton<IJobHandlerRegistration>(new JobHandlerRegistration<TJob>(
            new JobTypeRegistration(JobTypeName.For<TJob>())));
        return services;
    }

    public static IServiceCollection AddJobHandler<TJob, THandler>(
        this IServiceCollection services,
        string typeName,
        int payloadVersion = 1,
        Action<JobTypeRegistration>? configure = null)
        where THandler : class, IJobHandler<TJob>
    {
        ArgumentNullException.ThrowIfNull(services);
        var registration = new JobTypeRegistration(typeName, payloadVersion);
        if (!string.Equals(typeName, JobTypeName.For<TJob>(), StringComparison.Ordinal))
        {
            registration.AddAlias(JobTypeName.For<TJob>());
        }
        configure?.Invoke(registration);
        services.AddJobScheduler();
        services.AddScoped<IJobHandler<TJob>, THandler>();
        services.AddSingleton<IJobHandlerRegistration>(new JobHandlerRegistration<TJob>(registration));
        return services;
    }

    public static IServiceCollection AddJobPayloadSerializer<TSerializer>(this IServiceCollection services)
        where TSerializer : class, IJobPayloadSerializer
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddJobScheduler();
        services.Replace(ServiceDescriptor.Singleton<IJobPayloadSerializer, TSerializer>());
        return services;
    }

    internal interface IJobHandlerRegistration
    {
        void Register(JobHandlerRegistry registry);
    }

    private sealed class JobHandlerRegistration<TJob>(JobTypeRegistration type) : IJobHandlerRegistration
    {
        public void Register(JobHandlerRegistry registry) => registry.Add<TJob>(type);
    }
}
