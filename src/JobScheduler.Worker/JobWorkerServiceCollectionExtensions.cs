using JobScheduler.Worker;

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
        return services;
    }
}
