using JobScheduler.Worker;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class JobWorkerServiceCollectionExtensions
{
    public static IServiceCollection AddJobWorker(
        this IServiceCollection services,
        Action<JobWorkerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddJobScheduler();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.AddHostedService<JobWorker>();
        services.AddHostedService<DeadLetterMaintenanceService>();
        services.TryAddSingleton<JobWorkerState>();
        services.AddHealthChecks().AddCheck<JobWorkerHealthCheck>("job_scheduler_worker", tags: ["ready"]);
        return services;
    }

    public static IServiceCollection AddScheduleMaterializer(
        this IServiceCollection services,
        Action<ScheduleMaterializerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddJobScheduler();
        services.TryAddSingleton<JobWorkerState>();
        if (configure is not null) services.Configure(configure);
        services.AddHostedService<ScheduleMaterializer>();
        return services;
    }
}
