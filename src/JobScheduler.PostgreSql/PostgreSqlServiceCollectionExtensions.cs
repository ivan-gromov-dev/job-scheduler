using JobScheduler.Core.Jobs;
using JobScheduler.PostgreSql;
using JobScheduler.Core.Scheduling;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Microsoft.Extensions.DependencyInjection;

public static class PostgreSqlServiceCollectionExtensions
{
    public static IServiceCollection AddPostgreSqlJobStore(this IServiceCollection services, Action<PostgreSqlJobStoreOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(configure);
        var options = new PostgreSqlJobStoreOptions { ConnectionString = string.Empty }; configure(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionString);
        services.AddJobScheduler(); services.AddSingleton(options); services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(_ => NpgsqlDataSource.Create(options.ConnectionString));
        services.AddSingleton<PostgreSqlMigrator>(); services.AddSingleton<PostgreSqlJobStore>();
        services.AddSingleton<IJobStore>(provider => provider.GetRequiredService<PostgreSqlJobStore>());
        services.AddSingleton<PostgreSqlScheduleStore>();
        services.AddSingleton<IScheduleStore>(provider => provider.GetRequiredService<PostgreSqlScheduleStore>());
        return services;
    }
}
